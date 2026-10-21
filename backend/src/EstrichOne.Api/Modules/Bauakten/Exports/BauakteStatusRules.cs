namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>
/// Gemeinsame Regeln für Statuswechsel (Web, Sync aus den Apps, Automatik): eine Stelle für erlaubte Übergänge.
/// </summary>
public static class BauakteStatusRules
{
    /// <summary>Darf von <paramref name="from"/> nach <paramref name="to"/> gewechselt werden? Storno nur über Cancel (mit Grund).</summary>
    public static bool CanChange(BauakteStatus from, BauakteStatus to) =>
        from == to || (to != BauakteStatus.Cancelled && (from != BauakteStatus.Cancelled || to != BauakteStatus.Completed));

    /// <summary>Automatisch „In Ausführung“: nur aus „Geplant“.</summary>
    public static bool CanAutoStart(BauakteStatus status) => status == BauakteStatus.Planned;

    /// <summary>Automatisch abschließen (nach Abnahme): aus jedem laufenden Zustand.</summary>
    public static bool CanAutoComplete(BauakteStatus status) =>
        status is BauakteStatus.Planned or BauakteStatus.InProgress or BauakteStatus.Drying;
}

/// <summary>Auslöser für automatische Statuswechsel.</summary>
public enum BauakteAutoTrigger
{
    AssignmentDay,
    Photo,
    Diary,
    Inspection,
    Acceptance,
}

/// <summary>Automatische Statuswechsel nach den Einstellungen (Bereich Bauakte).</summary>
public interface IBauakteAutoStatus
{
    /// <summary>„Geplant“ → „In Ausführung“, falls der Auslöser eingeschaltet ist. Speichert mit dem nächsten SaveChanges.</summary>
    Task<bool> TryStartAsync(Guid bauakteId, BauakteAutoTrigger trigger, CancellationToken cancellationToken);

    /// <summary>Nach Abnahme abschließen, falls eingeschaltet. Speichert mit dem nächsten SaveChanges.</summary>
    Task<bool> TryCompleteAsync(Guid bauakteId, CancellationToken cancellationToken);
}

/// <summary>Welche Bauakten haben an einem Tag einen geplanten Einsatz (aus der Planung).</summary>
public interface IBauakteScheduleSource
{
    Task<IReadOnlyList<Guid>> ScheduledOnAsync(DateOnly day, CancellationToken cancellationToken);
}
