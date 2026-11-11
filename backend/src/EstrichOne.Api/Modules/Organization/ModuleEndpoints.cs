using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Organization;

/// <param name="Requires">Voraussetzungen (Schlüssel).</param>
/// <param name="RequiredBy">Module, die dieses Modul voraussetzen (Schlüssel).</param>
public sealed record ModuleStateResponse(
    string Key, string Area, string Title, string Description, bool Enabled, bool Core, IReadOnlyList<string> Requires, IReadOnlyList<string> RequiredBy);

/// <param name="Version">Version der Einstellungen (für <see cref="SaveModulesRequest.Version"/>).</param>
public sealed record ModulesResponse(IReadOnlyList<ModuleStateResponse> Modules, long Version);

/// <param name="Modules">Zu ändernde Schalter (Schlüssel → an/aus); nicht genannte Module bleiben unverändert.</param>
public sealed record SaveModulesRequest([property: Required] IReadOnlyDictionary<string, bool> Modules, long? Version = null);

/// <summary>Einstellungen › Module: Zustand für alle Angemeldeten (Web, Apps), Ändern nur mit <c>settings.manage</c>.</summary>
internal static class ModuleEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var modules = api.MapGroup("/settings/modules").WithTags("Einstellungen");
        modules.MapGet("/", GetAsync).WithName("GetModules")
            .WithSummary("Module der Suite mit Zustand, Bereich und Abhängigkeiten (für alle Angemeldeten; Apps blenden abgeschaltete Module aus)");
        modules.MapPut("/", SaveAsync).WithName("SaveModules")
            .WithSummary("Module ein- oder ausschalten (Abhängigkeiten werden geprüft, Daten bleiben erhalten, Eintrag im Protokoll)")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.SettingsManage);
    }

    internal static ModulesResponse Overview(ModuleSettings settings, long version) => new(
        AppModules.All.Select(m => new ModuleStateResponse(m.Key, m.Area, m.Title, m.Description, m.Core || settings.IsEnabled(m.Key), m.Core,
            m.Requires, AppModules.Dependents(m.Key).Select(d => d.Key).ToList())).ToList(),
        version);

    private static async Task<Ok<ModulesResponse>> GetAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var row = await db.Set<SystemSettingsRow>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(Overview(SystemSettingsStore.Parse(row?.Data).Modules, row?.Version ?? 0));
    }

    private static async Task<Results<Ok<ModulesResponse>, ProblemHttpResult>> SaveAsync(
        SaveModulesRequest request, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var row = await db.Set<SystemSettingsRow>().SingleOrDefaultAsync(cancellationToken);
        if (row is not null && request.Version is { } version && version != row.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Die Einstellungen wurden inzwischen geändert. Bitte neu laden.",
                extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT" });
        }

        var settings = SystemSettingsStore.Parse(row?.Data);
        var modules = settings.Modules;
        foreach (var (key, on) in request.Modules)
        {
            var module = AppModules.Find(key);
            if (module is null)
            {
                return Invalid("UNKNOWN_MODULE", $"Unbekanntes Modul „{key}“.");
            }

            if (module.Core)
            {
                if (!on)
                {
                    return Invalid("MODULE_CORE", $"„{module.Title}“ gehört zum Kern von EstrichONE und lässt sich nicht abschalten.");
                }

                continue;
            }

            modules = modules.With(key, on);
        }

        if (DependencyProblem(modules) is { } problem)
        {
            return Invalid("MODULE_DEPENDENCY", problem);
        }

        if (row is null)
        {
            row = new SystemSettingsRow();
            db.Add(row);
        }

        var saved = settings with { Modules = modules };
        RecordChanges(audit, currentUser.UserId, settings.Modules, modules);
        row.Update(JsonSerializer.Serialize(saved, SystemSettingsStore.Json));
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(Overview(modules, row.Version));
    }

    /// <summary>Erste verletzte Abhängigkeit als verständlicher Satz, sonst <c>null</c>.</summary>
    internal static string? DependencyProblem(ModuleSettings modules)
    {
        foreach (var module in AppModules.Switchable.Where(m => modules.IsEnabled(m.Key)))
        {
            foreach (var required in module.Requires.Where(r => !modules.IsEnabled(r)).Select(AppModules.Find).OfType<AppModuleInfo>())
            {
                return $"„{module.Title}“ benötigt „{required.Title}“. Bitte „{required.Title}“ ebenfalls einschalten oder „{module.Title}“ ausschalten.";
            }
        }

        return null;
    }

    /// <summary>Protokolleintrag, wenn sich Schalter geändert haben (auch beim Speichern aller Einstellungen).</summary>
    internal static void RecordChanges(IAuditLog audit, Guid actor, ModuleSettings before, ModuleSettings after)
    {
        var switchable = AppModules.Switchable.ToList();
        var enabled = switchable.Where(m => !before.IsEnabled(m.Key) && after.IsEnabled(m.Key)).Select(m => m.Key).ToList();
        var disabled = switchable.Where(m => before.IsEnabled(m.Key) && !after.IsEnabled(m.Key)).Select(m => m.Key).ToList();
        if (enabled.Count == 0 && disabled.Count == 0)
        {
            return;
        }

        audit.Record("settings.modules_changed", actor, "settings", "modules", new Dictionary<string, string?>
        {
            ["enabled"] = enabled.Count > 0 ? string.Join(", ", enabled) : null,
            ["disabled"] = disabled.Count > 0 ? string.Join(", ", disabled) : null,
        });
    }

    private static ProblemHttpResult Invalid(string code, string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Module", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>
/// Sperre für Endpunkte abgeschalteter Module (<see cref="AppModules.RequireModule{TBuilder}"/>): 409 <c>MODULE_DISABLED</c>
/// mit Modul und Titel. Administratoren (<c>settings.manage</c>) dürfen vorhandene Daten weiter lesen (GET).
/// Läuft nach Anmeldung und Autorisierung, damit Nicht-Angemeldete weiterhin 401 erhalten.
/// </summary>
internal static class ModuleGate
{
    public static IApplicationBuilder UseModuleGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var requirements = context.GetEndpoint()?.Metadata.GetOrderedMetadata<ModuleRequirement>();
        if (requirements is null || requirements.Count == 0)
        {
            await next();
            return;
        }

        var modules = (await context.RequestServices.GetRequiredService<ISystemSettings>().GetAsync(context.RequestAborted)).Modules;
        var blocked = requirements.FirstOrDefault(r => !r.AnyOf.Any(modules.IsEnabled));
        if (blocked is null)
        {
            await next();
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method) && context.User.Identity?.IsAuthenticated == true
            && await context.RequestServices.GetRequiredService<IUserPermissions>().HasAsync(Permissions.SettingsManage, context.RequestAborted))
        {
            await next();
            return;
        }

        var key = blocked.AnyOf[0];
        var title = string.Join(" / ", blocked.AnyOf.Select(k => AppModules.Find(k)?.Title ?? k));
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Modul deaktiviert",
                Detail = $"Das Modul „{title}“ ist deaktiviert (Einstellungen › Module). Vorhandene Daten bleiben erhalten.",
                Extensions = { ["code"] = AppModules.DisabledCode, ["module"] = key },
            },
        });
    });
}
