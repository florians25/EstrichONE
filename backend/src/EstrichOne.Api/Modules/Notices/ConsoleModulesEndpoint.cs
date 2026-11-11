using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Notices;

/// <summary>Ein Modul der Suite, wie es die Hosting-Konsole anzeigt.</summary>
/// <param name="Area">Bereich (Schlüssel: office, planning, material, hr, site, service, core).</param>
/// <param name="AreaTitle">Bereich als Text, z. B. „Büro“.</param>
/// <param name="Core">Kernmodul: immer eingeschaltet, nicht abschaltbar.</param>
/// <param name="Requires">Voraussetzungen (Schlüssel).</param>
public sealed record InstanceModuleState(
    string Key, string Area, string AreaTitle, string Title, string Description, bool Enabled, bool Core, IReadOnlyList<string> Requires);

/// <param name="Modules">Alle Module der Suite in Anzeigereihenfolge (Kernmodule zuerst).</param>
public sealed record InstanceModulesResponse(IReadOnlyList<InstanceModuleState> Modules);

/// <summary>
/// Modulkatalog mit Zustand für die Hosting-Konsole (<c>GET /api/v1/system/modules</c>). Abgesichert wie das Setzen der
/// Systemhinweise: <c>Authorization: Bearer &lt;Instanz-Schlüssel&gt;</c>; ohne konfigurierten Schlüssel 404. Nur lesend.
/// Die Konsole braucht so weder eine Kopie des Katalogs noch Datenbankzugriff auf die Instanz.
/// </summary>
internal static class ConsoleModulesEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/system/modules", GetAsync)
            .WithTags("Systemhinweise")
            .WithName("GetInstanceModules")
            .WithSummary("Module der Suite mit Zustand – nur Hosting-Konsole (Authorization: Bearer <Instanz-Schlüssel>)")
            .AllowAnonymous().RequireRateLimiting("auth");

    private static async Task<Results<Ok<InstanceModulesResponse>, NotFound, UnauthorizedHttpResult>> GetAsync(
        HttpContext http, IOptions<NoticeSettings> options, ISystemSettings settings, CancellationToken cancellationToken)
    {
        var token = options.Value.ManagementToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return TypedResults.NotFound();
        }

        if (!NoticesModule.IsAuthorized(http.Request.Headers.Authorization.ToString(), token))
        {
            return TypedResults.Unauthorized();
        }

        var modules = (await settings.GetAsync(cancellationToken)).Modules;
        return TypedResults.Ok(new InstanceModulesResponse(AppModules.All
            .Select(m => new InstanceModuleState(m.Key, m.Area, AppModules.AreaTitle(m.Area), m.Title, m.Description, m.Core || modules.IsEnabled(m.Key), m.Core, m.Requires))
            .ToList()));
    }
}
