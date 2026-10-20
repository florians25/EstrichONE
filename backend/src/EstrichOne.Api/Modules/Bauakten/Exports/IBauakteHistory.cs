namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>
/// Verlauf einer Bauakte (docs/reengineering/02: „aus dem Audit-Log abgeleitet“). Gespeichert wird nur, was
/// passiert ist und durch wen – nie Inhalte wie Notiztexte.
/// </summary>
public interface IBauakteHistory
{
    /// <summary>Wird mit dem nächsten <c>SaveChanges</c> gespeichert (gleiche Transaktion).</summary>
    void Record(Guid bauakteId, string action, IReadOnlyDictionary<string, string?>? details = null);
}

public static class BauakteHistoryActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string StatusChanged = "status_changed";
    public const string Deleted = "deleted";
    public const string NoteAdded = "note_added";
    public const string FileAdded = "file_added";
    public const string WorkItemCreated = "workitem_created";
    public const string WorkItemDone = "workitem_done";
}
