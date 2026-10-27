namespace EstrichOne.Api.Modules.Documentation.Exports;

/// <summary>Welche Dateien im Archiv liegen oder dafür vorgemerkt sind (deren Inhalt darf sich nie mehr ändern).</summary>
public interface IFileArchiveState
{
    Task<IReadOnlySet<Guid>> ArchivedOrQueuedAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken);
}
