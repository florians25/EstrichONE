using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Einstellungen der Warnungen (Personal › Einstellungen › Warnungen).</summary>
/// <param name="CertificateDueDay">AU-Nachweis ab diesem Kalendertag erwartet (Standard 4).</param>
/// <param name="FrequentShortThreshold">Hinweis ab so vielen Kurzerkrankungen in 12 Monaten; 0 = aus.</param>
public sealed record AbsenceWarningSettings(
    bool Enabled = true, bool PlanningImpact = true, int PlanningImpactDays = 14, bool Certificate = true,
    int CertificateDueDay = 4, bool ContinuedPay = true, bool Bem = true, bool VacationOverlap = true,
    int FrequentShortThreshold = 0, bool Adjacent = false, bool NotifyHr = true, bool NotifyPlanners = true,
    bool NotifyCrewLeaders = false);

public sealed record AcknowledgeAbsenceWarning([property: Required, MaxLength(200)] string Key);

/// <param name="RetrievedOn">eAU bei der Krankenkasse abgerufen am (leer = Markierung entfernen).</param>
public sealed record CertificateRetrievedRequest(DateOnly? RetrievedOn);

/// <summary>
/// Warnungen bei Krankmeldungen und Abwesenheiten. Datenschutz: Gesundheitsbezogene Hinweise (AU, Entgeltfortzahlung, BEM, Häufung,
/// Urlaubsüberschneidung) nur mit „hr.manage“; die Disposition sieht nur „abwesend“ mit den betroffenen Einsätzen. Keine Diagnosen.
/// </summary>
internal static class AbsenceWarningEndpoints
{
    public static void Map(RouteGroupBuilder hr)
    {
        hr.MapGet("/absence-warnings", ListAsync).WithName("ListAbsenceWarnings")
            .WithSummary("Warnungen: Abwesenheit betrifft Einsätze (Planung), AU fehlt, Entgeltfortzahlung, BEM, Überschneidung mit Urlaub (nur Personal)")
            .ProducesProblem(StatusCodes.Status403Forbidden).RequirePermission(Permissions.AbsenceRequest);
        hr.MapPost("/absence-warnings/acknowledge", AcknowledgeAsync).WithName("AcknowledgeAbsenceWarning")
            .WithSummary("Warnung zur Kenntnis nehmen (für alle ausblenden, bis sich der Sachverhalt ändert)")
            .ProducesProblem(StatusCodes.Status403Forbidden).RequirePermission(Permissions.AbsenceRequest);
        hr.MapGet("/settings/warnings", GetSettingsAsync).WithName("GetAbsenceWarningSettings").RequirePermission(Permissions.HrManage);
        hr.MapPut("/settings/warnings", SaveSettingsAsync).WithName("SaveAbsenceWarningSettings")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);
        hr.MapPost("/sick-notes/{id:guid}/certificate-retrieved", RetrievedAsync).WithName("MarkCertificateRetrieved")
            .WithSummary("eAU als abgerufen markieren (Datum) oder Markierung entfernen")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HrManage);
    }

    internal static AbsenceWarningSettings ToView(HrSettings s) => new(s.WarningsEnabled, s.WarnPlanningImpact, s.PlanningImpactDays, s.WarnCertificate,
        s.CertificateDueDay, s.WarnContinuedPay, s.WarnBem, s.WarnVacationOverlap, s.FrequentShortThreshold, s.WarnAdjacent, s.NotifyHr, s.NotifyPlanners,
        s.NotifyCrewLeaders);

    /// <summary>Sichtbarkeit: Personal alles, Planung nur „betrifft Einsätze“.</summary>
    internal static List<AbsenceWarning> Visible(IEnumerable<AbsenceWarning> items, bool health, bool planning) =>
        items.Where(w => AbsenceWarningKinds.IsHealthRelated(w.Kind) ? health : planning).ToList();

    private static async Task<Results<Ok<AbsenceWarningList>, ProblemHttpResult>> ListAsync(AbsenceWarningService warnings, HrService hr, IUserPermissions permissions,
        CancellationToken cancellationToken)
    {
        var health = await permissions.HasAsync(Permissions.HrManage, cancellationToken);
        var planning = await permissions.HasAsync(Permissions.PlanningManage, cancellationToken);
        if (!health && !planning)
        {
            return HrEndpoints.Problem(StatusCodes.Status403Forbidden, "Keine Berechtigung", "Warnungen sehen Personalverwaltung und Disposition (Einsätze planen).", "FORBIDDEN");
        }

        var settings = await hr.SettingsAsync(cancellationToken);
        var (items, acknowledged) = await warnings.ComputeAsync(cancellationToken);
        return TypedResults.Ok(new AbsenceWarningList(settings.WarningsEnabled, health, Visible(items, health, planning || health), health ? acknowledged : 0));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> AcknowledgeAsync(AcknowledgeAbsenceWarning request, EstrichOneDbContext db,
        IUserPermissions permissions, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock, CancellationToken cancellationToken)
    {
        var key = request.Key.Trim();
        var planningKey = key.StartsWith("plan:", StringComparison.Ordinal);
        var allowed = await permissions.HasAsync(Permissions.HrManage, cancellationToken)
            || (planningKey && await permissions.HasAsync(Permissions.PlanningManage, cancellationToken));
        if (!allowed)
        {
            return HrEndpoints.Problem(StatusCodes.Status403Forbidden, "Keine Berechtigung", null, "FORBIDDEN");
        }

        if (!await db.Set<AbsenceWarningAck>().AnyAsync(a => a.Key == key, cancellationToken))
        {
            db.Add(AbsenceWarningAck.Create(key, currentUser.UserId, clock.GetUtcNow()));
            // Protokoll ohne Inhalt der Warnung, nur Art des Schlüssels.
            audit.Record("hr.absence_warning.acknowledged", currentUser.UserId, "absence_warning", key.Split(':')[0], null);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<Ok<AbsenceWarningSettings>> GetSettingsAsync(HrService hr, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToView(await hr.SettingsAsync(cancellationToken)));

    private static async Task<Results<Ok<AbsenceWarningSettings>, ProblemHttpResult>> SaveSettingsAsync(AbsenceWarningSettings request, EstrichOneDbContext db,
        IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var settings = await db.Set<HrSettings>().SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new HrSettings();
            db.Add(settings);
        }

        try
        {
            settings.UpdateWarnings(request.Enabled, request.PlanningImpact, request.PlanningImpactDays, request.Certificate, request.CertificateDueDay,
                request.ContinuedPay, request.Bem, request.VacationOverlap, request.FrequentShortThreshold, request.Adjacent, request.NotifyHr,
                request.NotifyPlanners, request.NotifyCrewLeaders);
        }
        catch (ArgumentException exception)
        {
            return HrEndpoints.Validation(exception.Message);
        }

        audit.Record("hr.settings.warnings_saved", currentUser.UserId, "hr_settings", settings.Id.ToString(), new Dictionary<string, string?>
        {
            ["enabled"] = settings.WarningsEnabled ? "ja" : "nein", ["certificateDueDay"] = settings.CertificateDueDay.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["frequentShort"] = settings.FrequentShortThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["adjacent"] = settings.WarnAdjacent ? "ja" : "nein",
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToView(settings));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RetrievedAsync(Guid id, CertificateRetrievedRequest request, EstrichOneDbContext db,
        IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var item = await db.Set<Absence>().SingleOrDefaultAsync(a => a.Id == id && a.Kind == AbsenceKind.Sick, cancellationToken);
        if (item is null)
        {
            return HrEndpoints.Problem(StatusCodes.Status404NotFound, "Krankmeldung nicht gefunden", null, "NOT_FOUND");
        }

        item.WithRetrievedCertificate(request.RetrievedOn);
        audit.Record("hr.sick_note.certificate_retrieved", currentUser.UserId, "absence", id.ToString(), new Dictionary<string, string?>
        {
            ["userId"] = item.UserId.ToString(), ["eau"] = request.RetrievedOn is null ? "nein" : "ja",
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}

/// <summary>
/// Benachrichtigungen zu Warnungen. Titel und Text erscheinen in der Pushnachricht (Sperrbildschirm) – deshalb nie „krank“,
/// keine Fristen mit Gesundheitsbezug, nur Name, Zeitraum „abwesend“ und betroffene Einsätze bzw. „bitte prüfen“.
/// </summary>
internal static class AbsenceWarningNotifications
{
    private const string HrLink = "/personal/urlaub?reiter=krank";

    /// <summary>Hinweise, die die Personalverwaltung als Benachrichtigung erhält (Häufung und angrenzende Tage nur in der Liste).</summary>
    private static readonly HashSet<string> PushedHealthKinds =
        [AbsenceWarningKinds.Certificate, AbsenceWarningKinds.ContinuedPay, AbsenceWarningKinds.Bem, AbsenceWarningKinds.VacationOverlap];

    private static async Task<HashSet<Guid>> HrAsync(HrSettings settings, IRoleAssignments roles, CancellationToken cancellationToken) =>
        settings.NotifyHr ? (await roles.GetUserIdsWithPermissionAsync(Permissions.HrManage, cancellationToken)).ToHashSet() : [];

    private static async Task<HashSet<Guid>> PlanningRecipientsAsync(AbsenceWarning warning, HrSettings settings, IRoleAssignments roles, EstrichOneDbContext db,
        CancellationToken cancellationToken)
    {
        var ids = await HrAsync(settings, roles, cancellationToken);
        if (settings.NotifyPlanners)
        {
            ids.UnionWith(await roles.GetUserIdsWithPermissionAsync(Permissions.PlanningManage, cancellationToken));
        }

        if (settings.NotifyCrewLeaders)
        {
            var crewIds = warning.Assignments.Select(a => a.CrewId).OfType<Guid>().Distinct().ToList();
            ids.UnionWith(await db.Set<Crew>().AsNoTracking().Where(c => crewIds.Contains(c.Id) && c.LeaderUserId != null).Select(c => c.LeaderUserId!.Value)
                .ToListAsync(cancellationToken));
        }

        ids.Remove(warning.UserId); // die abwesende Person selbst nicht
        return ids;
    }

    public static Task NotifyImpactAsync(AbsenceWarning warning, HashSet<Guid> recipients, INotifier notifier, CancellationToken cancellationToken) =>
        recipients.Count == 0 ? Task.CompletedTask : notifier.NotifyAsync(recipients, new NotificationRequest(NotificationCategory.Absence,
            "Abwesenheit betrifft Einsätze", $"{warning.Title}: {warning.Text}", warning.Link, "absence", warning.AbsenceId,
            warning.Severity == "error" ? NotificationUrgency.High : NotificationUrgency.Normal, $"absence-impact:{warning.Key}", NotificationEvents.AbsencePlanningImpact),
            cancellationToken);

    /// <summary>Nach dem Speichern einer Krankmeldung: Disposition und Personal sofort über betroffene Einsätze informieren.</summary>
    public static async Task SickNoteSavedAsync(Absence absence, string userName, bool created, AbsenceWarningService warnings, HrService hr, IRoleAssignments roles,
        INotifier notifier, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var settings = await hr.SettingsAsync(cancellationToken);
        if (!settings.WarningsEnabled)
        {
            return;
        }

        if (await warnings.ImpactAsync(absence.Id, cancellationToken) is { } impact)
        {
            await NotifyImpactAsync(impact, await PlanningRecipientsAsync(impact, settings, roles, db, cancellationToken), notifier, cancellationToken);
            return;
        }

        if (created && absence.To >= hr.Today)
        {
            var ids = await HrAsync(settings, roles, cancellationToken);
            ids.Remove(absence.UserId);
            if (ids.Count > 0)
            {
                await notifier.NotifyAsync(ids, new NotificationRequest(NotificationCategory.Absence, "Neue Abwesenheitsmeldung",
                    $"{userName}, {AbsenceWarningRules.Range(absence.From, absence.To)}", HrLink, "absence", absence.Id, NotificationUrgency.Normal,
                    $"absence-new:{absence.Id}", NotificationEvents.AbsenceWarning), cancellationToken);
            }
        }
    }

    /// <summary>Tägliche Prüfung: neue Hinweise an die Personalverwaltung, Einsätze heute/morgen an die Disposition (je Warnung einmal).</summary>
    public static async Task<int> ScanAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var system = await services.GetRequiredService<ISystemSettings>().GetAsync(cancellationToken);
        if (!system.Modules.Absence)
        {
            return 0;
        }

        var hr = services.GetRequiredService<HrService>();
        var settings = await hr.SettingsAsync(cancellationToken);
        if (!settings.WarningsEnabled)
        {
            return 0;
        }

        var db = services.GetRequiredService<EstrichOneDbContext>();
        var roles = services.GetRequiredService<IRoleAssignments>();
        var notifier = services.GetRequiredService<INotifier>();
        var (items, _) = await services.GetRequiredService<AbsenceWarningService>().ComputeAsync(cancellationToken);
        var count = 0;
        var hrIds = await HrAsync(settings, roles, cancellationToken);
        foreach (var w in items.Where(w => PushedHealthKinds.Contains(w.Kind) && w.Severity != "info"))
        {
            var ids = hrIds.Where(id => id != w.UserId).ToList();
            if (ids.Count == 0)
            {
                continue;
            }

            await notifier.NotifyAsync(ids, new NotificationRequest(NotificationCategory.Absence, "Personalhinweis",
                $"{w.UserName}: Hinweis zur Abwesenheit. Bitte in EstrichONE unter Personal prüfen.", HrLink, "absenceWarning", w.AbsenceId, NotificationUrgency.Normal,
                $"absence-warning:{w.Key}", NotificationEvents.AbsenceWarning), cancellationToken);
            count++;
        }

        var tomorrow = hr.Today.AddDays(1);
        foreach (var w in items.Where(w => w.Kind == AbsenceWarningKinds.PlanningImpact && w.Date <= tomorrow))
        {
            await NotifyImpactAsync(w, await PlanningRecipientsAsync(w, settings, roles, db, cancellationToken), notifier, cancellationToken);
            count++;
        }

        return count;
    }
}

/// <summary>Prüft stündlich (7 bis 20 Uhr) die Warnungen und benachrichtigt einmal je Warnung.</summary>
internal sealed partial class AbsenceWarningScanner(IServiceScopeFactory scopes, TimeProvider clock, ILogger<AbsenceWarningScanner> logger) : BackgroundService
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), clock, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
            do
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogScanFailed(logger, exception);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren.
        }
    }

    /// <param name="anyTime">Auch nachts prüfen (Tests).</param>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken, bool anyTime = false)
    {
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin);
        if (!anyTime && local.Hour is < 7 or >= 20)
        {
            return 0; // nicht nachts melden
        }

        await using var scope = scopes.CreateAsyncScope();
        return await AbsenceWarningNotifications.ScanAsync(scope.ServiceProvider, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Prüfung der Abwesenheitswarnungen fehlgeschlagen")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);
}
