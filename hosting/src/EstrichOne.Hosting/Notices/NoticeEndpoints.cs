using EstrichOne.Hosting.Api;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Hosting.Notices;

public sealed record NoticeInput(
    NoticeKind? Kind,
    string? Title,
    string? Text,
    string? Severity,
    DateTime? StartsAt,
    DateTime? EndsAt,
    bool? AdminsMayWork,
    bool? AllTenants,
    IReadOnlyList<Guid>? TenantIds);

public sealed record NoticeTarget(Guid TenantId, string Slug, string CompanyName, bool? Synced, string? Error, DateTime? At);

public sealed record NoticeDto(
    Guid Id, NoticeKind Kind, string Title, string Text, string Severity, DateTime? StartsAt, DateTime? EndsAt, bool AdminsMayWork,
    bool AllTenants, IReadOnlyList<Guid> TenantIds, string Status, string CreatedBy, DateTime CreatedAt, IReadOnlyList<NoticeTarget> Targets);

/// <summary>Systemhinweise: Ankündigungen und Wartungsfenster für einzelne, ausgewählte oder alle Instanzen.</summary>
public static class NoticeEndpoints
{
    private static readonly string[] Severities = ["info", "warning", "critical"];

    public static void Map(IEndpointRouteBuilder app)
    {
        var notices = app.MapGroup("/api/notices").WithTags("Systemhinweise").RequireAuthorization();
        notices.MapGet("/", ListAsync);
        notices.MapPost("/", (NoticeInput input, HostingDbContext db, AuditLog audit, NoticeDistributor distributor, TimeProvider clock, CancellationToken ct) =>
            SaveAsync(null, input, db, audit, distributor, clock, ct));
        notices.MapPut("/{id:guid}", (Guid id, NoticeInput input, HostingDbContext db, AuditLog audit, NoticeDistributor distributor, TimeProvider clock, CancellationToken ct) =>
            SaveAsync(id, input, db, audit, distributor, clock, ct));
        notices.MapPost("/{id:guid}/end", async (Guid id, HostingDbContext db, AuditLog audit, NoticeDistributor distributor, TimeProvider clock, CancellationToken ct) =>
        {
            var notice = await db.Notices.FindAsync([id], ct);
            if (notice is null)
            {
                return ApiErrors.NotFound("Hinweis nicht gefunden.");
            }

            var now = clock.GetUtcNow().UtcDateTime;
            notice.EndsAt = now;
            if (notice.StartsAt > now)
            {
                notice.StartsAt = now;
            }

            notice.UpdatedAt = now;
            await audit.WriteAsync(notice.Kind == NoticeKind.Maintenance ? "Wartung beendet" : "Ankündigung beendet", details: notice.Title, cancellationToken: ct);
            await distributor.DistributeAsync(force: false, ct);
            return Results.NoContent();
        });
        notices.MapDelete("/{id:guid}", async (Guid id, HostingDbContext db, AuditLog audit, NoticeDistributor distributor, CancellationToken ct) =>
        {
            var notice = await db.Notices.FindAsync([id], ct);
            if (notice is null)
            {
                return ApiErrors.NotFound("Hinweis nicht gefunden.");
            }

            db.Notices.Remove(notice);
            await audit.WriteAsync("Systemhinweis gelöscht", details: notice.Title, cancellationToken: ct);
            await distributor.DistributeAsync(force: false, ct);
            return Results.NoContent();
        });
        notices.MapPost("/sync", async (NoticeDistributor distributor, AuditLog audit, CancellationToken ct) =>
        {
            await distributor.DistributeAsync(force: true, ct);
            await audit.WriteAsync("Systemhinweise neu verteilt", cancellationToken: ct);
            return Results.NoContent();
        });
    }

    public static string StatusOf(Notice notice, DateTime now) =>
        notice.EndsAt <= now ? "beendet" : notice.StartsAt > now ? "geplant" : "aktiv";

    private static async Task<IResult> ListAsync(HostingDbContext db, NoticeDistributor distributor, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.State != TenantState.Deleted).OrderBy(t => t.CompanyName).ToListAsync(cancellationToken);
        var notices = (await db.Notices.AsNoTracking().ToListAsync(cancellationToken))
            .OrderBy(n => n.EndsAt <= now).ThenByDescending(n => n.StartsAt ?? n.CreatedAt).ToList();
        return Results.Ok(notices.Select(n => new NoticeDto(n.Id, n.Kind, n.Title, n.Text, n.Severity, n.StartsAt, n.EndsAt, n.AdminsMayWork,
            n.AllTenants, [.. n.Targets()], StatusOf(n, now), n.CreatedBy, n.CreatedAt,
            tenants.Where(t => n.AppliesTo(t.Id)).Select(t =>
            {
                var state = distributor.States.GetValueOrDefault(t.Id);
                return new NoticeTarget(t.Id, t.Slug, t.CompanyName, state?.Success, state?.Error, state?.At);
            }).ToList())));
    }

    private static async Task<IResult> SaveAsync(Guid? id, NoticeInput input, HostingDbContext db, AuditLog audit, NoticeDistributor distributor,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        if (input.Kind is null) errors["kind"] = "Bitte Art wählen (Ankündigung oder Wartung).";
        if (Validation.CheckText(input.Title?.Trim(), "Der Titel", 120, true) is { } titleError) errors["title"] = titleError;
        if (input.Text is { Length: > 2000 }) errors["text"] = "Der Text ist zu lang (höchstens 2000 Zeichen).";
        var severity = string.IsNullOrWhiteSpace(input.Severity) ? "info" : input.Severity;
        if (!Severities.Contains(severity)) errors["severity"] = "Schweregrad: info, warning oder critical.";
        var starts = input.StartsAt?.ToUniversalTime();
        var ends = input.EndsAt?.ToUniversalTime();
        if (starts is not null && ends is not null && ends <= starts) errors["endsAt"] = "Das Ende muss nach dem Beginn liegen.";
        if (input.Kind == NoticeKind.Maintenance && starts is null) errors["startsAt"] = "Bitte den Beginn der Wartung angeben.";
        var all = input.AllTenants != false;
        var targets = (input.TenantIds ?? []).Distinct().ToList();
        if (!all && targets.Count == 0) errors["tenantIds"] = "Bitte mindestens eine Instanz wählen.";
        if (!all && await db.Tenants.CountAsync(t => targets.Contains(t.Id), cancellationToken) != targets.Count) errors["tenantIds"] = "Unbekannte Instanz gewählt.";
        if (errors.Count > 0)
        {
            return ApiErrors.Validation(errors);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        Notice? notice;
        if (id is null)
        {
            notice = new Notice { Title = string.Empty, CreatedBy = audit.CurrentActor, CreatedAt = now };
            db.Notices.Add(notice);
        }
        else
        {
            notice = await db.Notices.FindAsync([id.Value], cancellationToken);
            if (notice is null)
            {
                return ApiErrors.NotFound("Hinweis nicht gefunden.");
            }
        }

        notice.Kind = input.Kind!.Value;
        notice.Title = input.Title!.Trim();
        notice.Text = input.Text?.Trim() ?? string.Empty;
        notice.Severity = severity;
        notice.StartsAt = starts;
        notice.EndsAt = ends;
        notice.AdminsMayWork = input.AdminsMayWork != false;
        notice.AllTenants = all;
        notice.TenantIds = all ? string.Empty : string.Join(',', targets);
        notice.UpdatedAt = now;
        var label = notice.Kind == NoticeKind.Maintenance ? "Wartung" : "Ankündigung";
        await audit.WriteAsync(id is null ? $"{label} angelegt" : $"{label} geändert",
            details: $"{notice.Title} · {(all ? "alle Instanzen" : $"{targets.Count} Instanz(en)")} · {starts:yyyy-MM-dd HH:mm}–{ends:yyyy-MM-dd HH:mm} UTC",
            cancellationToken: cancellationToken);
        await distributor.DistributeAsync(force: false, cancellationToken);
        return id is null ? Results.Created($"/api/notices/{notice.Id}", new { notice.Id }) : Results.NoContent();
    }
}
