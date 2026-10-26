using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Notices;

/// <summary>
/// Systemhinweise: Ankündigungen (Banner) und Wartungsmodus. Lesen ist ohne Anmeldung möglich (Clients zeigen Banner
/// bzw. Wartungsseite auch vor der Anmeldung); gesetzt werden sie von der Hosting-Konsole mit einem Instanz-Schlüssel.
/// </summary>
public sealed partial class NoticesModule : IModule
{
    public const int MaxAnnouncements = 20;
    private static readonly string[] Severities = ["info", "warning", "critical"];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NoticeSettings>().Bind(configuration.GetSection(NoticeSettings.SectionName));
        services.AddSingleton<NoticeStore>();
        services.AddTransient<IStartupFilter, MaintenanceStartupFilter>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        var notices = api.MapGroup("/system/notices").WithTags("Systemhinweise");

        notices.MapGet("/", async (NoticeStore store, HttpContext http, IServiceProvider services, CancellationToken ct) =>
            {
                // Angemeldet: auch Hinweise an die eigenen Rollen.
                IReadOnlyCollection<Guid>? roles = null;
                if (http.User.Identity?.IsAuthenticated == true)
                {
                    var userId = services.GetRequiredService<ICurrentUser>().UserId;
                    roles = await services.GetRequiredService<IRoleAssignments>().GetRoleIdsAsync(userId, ct);
                }

                return TypedResults.Ok(store.ForClients(roles));
            })
            .WithName("GetSystemNotices")
            .WithSummary("Aktuelle Ankündigungen und geplante bzw. laufende Wartung (ohne Anmeldung lesbar)")
            .AllowAnonymous();

        notices.MapPut("/", Results<NoContent, NotFound, UnauthorizedHttpResult, ValidationProblem> (
                SystemNoticesDocument document, HttpContext http, NoticeStore store, IOptions<NoticeSettings> options,
                ILogger<NoticesModule> logger) =>
            {
                var token = options.Value.ManagementToken;
                if (string.IsNullOrWhiteSpace(token))
                {
                    return TypedResults.NotFound();
                }

                if (!IsAuthorized(http.Request.Headers.Authorization.ToString(), token))
                {
                    return TypedResults.Unauthorized();
                }

                var errors = Validate(document);
                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                store.Replace(document);
                LogReplaced(logger, document.Announcements.Count, document.Maintenance is not null);
                return TypedResults.NoContent();
            })
            .WithName("SetSystemNotices")
            .WithSummary("Systemhinweise vollständig ersetzen – nur Hosting-Konsole (Authorization: Bearer <Instanz-Schlüssel>)")
            .AllowAnonymous();

        // Administratoren der Instanz (Recht settings.manage): eigene Hinweise und Wartung; die der Konsole nur lesen.
        var admin = notices.MapGroup("/admin").RequirePermission(Permissions.SettingsManage);
        admin.MapGet("/", (NoticeStore store, IOptions<NoticeSettings> options, TimeProvider clock) =>
                TypedResults.Ok(new AdminNoticesResponse(store.Instance, store.Current, !string.IsNullOrWhiteSpace(options.Value.ManagementToken), clock.GetUtcNow())))
            .WithName("GetAdminNotices").WithSummary("Hinweise und Wartung der Instanz (bearbeitbar) und der Hosting-Konsole (nur lesen)");
        admin.MapPut("/announcements/{id}", SaveAnnouncementAsync).WithName("SaveAdminAnnouncement")
            .WithSummary("Ankündigung anlegen oder ändern; optional per E-Mail an alle aktiven Benutzer (bzw. die Zielgruppe)")
            .ProducesValidationProblem();
        admin.MapDelete("/announcements/{id}", async Task<Results<NoContent, NotFound>> (string id, NoticeStore store, IAuditLog audit,
                ICurrentUser current, EstrichOneDbContext db, CancellationToken ct) =>
            {
                var doc = store.Instance;
                if (doc.Announcements.All(a => a.Id != id))
                {
                    return TypedResults.NotFound();
                }

                await store.ReplaceInstanceAsync(doc with { Announcements = doc.Announcements.Where(a => a.Id != id).ToList() }, ct);
                audit.Record("notices.announcement_deleted", current.UserId, "notice", id);
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteAdminAnnouncement");
        admin.MapPut("/maintenance", async Task<Results<Ok<MaintenanceWindow>, ValidationProblem>> (MaintenanceWindow window, NoticeStore store,
                IAuditLog audit, ICurrentUser current, EstrichOneDbContext db, CancellationToken ct) =>
            {
                var doc = store.Instance with { Maintenance = window with { Message = string.IsNullOrWhiteSpace(window.Message) ? null : window.Message.Trim() } };
                var errors = Validate(doc);
                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                await store.ReplaceInstanceAsync(doc, ct);
                audit.Record("notices.maintenance_set", current.UserId, "notice", "maintenance", new Dictionary<string, string?>
                {
                    ["startsAt"] = window.StartsAt.ToString("O"), ["endsAt"] = window.EndsAt?.ToString("O"), ["adminsMayWork"] = window.AdminsMayWork.ToString(),
                });
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(doc.Maintenance!);
            })
            .WithName("SetAdminMaintenance").WithSummary("Wartung der Instanz planen oder sofort starten (Beginn ≤ jetzt)");
        admin.MapDelete("/maintenance", async (NoticeStore store, IAuditLog audit, ICurrentUser current, EstrichOneDbContext db, CancellationToken ct) =>
            {
                await store.ReplaceInstanceAsync(store.Instance with { Maintenance = null }, ct);
                audit.Record("notices.maintenance_ended", current.UserId, "notice", "maintenance");
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("EndAdminMaintenance").WithSummary("Wartung der Instanz beenden bzw. Planung verwerfen");
    }

    private static async Task<Results<Ok<SaveAnnouncementResponse>, ValidationProblem>> SaveAnnouncementAsync(
        string id, SaveAnnouncementRequest request, NoticeStore store, IAuditLog audit, ICurrentUser current, IMailService mail,
        EstrichOneDbContext db, IRoleAssignments roles, CancellationToken ct)
    {
        var announcement = new SystemAnnouncement(id, request.Title?.Trim() ?? "", request.Text?.Trim() ?? "", request.Severity, request.StartsAt, request.EndsAt,
            request.RoleIds is { Count: > 0 } ? request.RoleIds.Distinct().ToList() : null, "instance", request.Notify);
        var doc = store.Instance;
        var existed = doc.Announcements.Any(a => a.Id == id);
        doc = doc with { Announcements = [.. doc.Announcements.Where(a => a.Id != id), announcement] };
        var errors = Validate(doc);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await store.ReplaceInstanceAsync(doc, ct);
        audit.Record(existed ? "notices.announcement_updated" : "notices.announcement_created", current.UserId, "notice", id,
            new Dictionary<string, string?> { ["title"] = announcement.Title, ["severity"] = announcement.Severity, ["email"] = request.SendEmail.ToString() });
        await db.SaveChangesAsync(ct);

        var sent = 0;
        if (request.SendEmail && await mail.IsConfiguredAsync(ct))
        {
            var users = await db.Set<User>().AsNoTracking().Where(u => u.IsActive && u.Email != null).Select(u => new { u.Id, u.Email }).ToListAsync(ct);
            foreach (var user in users)
            {
                if (announcement.RoleIds is { Count: > 0 } target && !(await roles.GetRoleIdsAsync(user.Id, ct)).Any(target.Contains))
                {
                    continue;
                }

                if (await mail.QueueAsync(new MailRequest(user.Email!, announcement.Title, announcement.Text, "notice", $"notice:{id}"), ct) is not null)
                {
                    sent++;
                }
            }
        }

        return TypedResults.Ok(new SaveAnnouncementResponse(announcement, sent));
    }

    private static bool IsAuthorized(string header, string token)
    {
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(header[prefix.Length..].Trim()));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static Dictionary<string, string[]> Validate(SystemNoticesDocument document)
    {
        var errors = new Dictionary<string, string[]>();
        var announcements = document.Announcements ?? [];
        if (announcements.Count > MaxAnnouncements)
        {
            errors["announcements"] = [$"Höchstens {MaxAnnouncements} Ankündigungen."];
        }

        for (var i = 0; i < announcements.Count; i++)
        {
            var a = announcements[i];
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(a.Id) || a.Id.Length > 64) problems.Add("Id fehlt oder ist zu lang.");
            if (string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 120) problems.Add("Titel fehlt oder ist länger als 120 Zeichen.");
            if (a.Text is null || a.Text.Length > 2000) problems.Add("Text fehlt oder ist länger als 2000 Zeichen.");
            if (!Severities.Contains(a.Severity)) problems.Add("Schweregrad muss info, warning oder critical sein.");
            if (a.StartsAt is not null && a.EndsAt is not null && a.EndsAt <= a.StartsAt) problems.Add("Ende liegt vor dem Beginn.");
            if (problems.Count > 0)
            {
                errors[$"announcements[{i}]"] = [.. problems];
            }
        }

        if (document.Maintenance is { } window)
        {
            if (window.EndsAt is not null && window.EndsAt <= window.StartsAt) errors["maintenance"] = ["Ende liegt vor dem Beginn."];
            if (window.Message is { Length: > 2000 }) errors["maintenance.message"] = ["Text ist länger als 2000 Zeichen."];
        }

        return errors;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Systemhinweise gesetzt: {Count} Ankündigung(en), Wartung geplant: {Maintenance}.")]
    private static partial void LogReplaced(ILogger logger, int count, bool maintenance);

    private sealed class MaintenanceStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseMiddleware<MaintenanceGate>();
            next(app);
        };
    }
}
