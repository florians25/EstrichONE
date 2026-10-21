namespace EstrichOne.Api.Modules.Documentation.Exports;

/// <summary>Dokument, Foto oder Video in der für die Archivierung nötigen Form (Lesesicht, EF-übersetzbar).</summary>
public sealed class ArchivableDocument
{
    public Guid FileId { get; init; }
    public Guid? BauakteId { get; init; }

    /// <summary>„document“, „photo“ oder „video“.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Dokumentart (z. B. invoice, offer); bei Fotos und Videos leer.</summary>
    public string? Category { get; init; }
    public string OriginalName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Zeitpunkt der (letzten) Freigabe, falls freigegeben.</summary>
    public DateTimeOffset? ApprovedAt { get; init; }
}

/// <summary>Zugriff des Archiv-Moduls auf gespeicherte (vollständig hochgeladene, nicht gelöschte) Dateien.</summary>
public interface IArchivableDocuments
{
    /// <summary>Abfrage über alle archivierbaren Dateien; andere Module können sie mit eigenen Tabellen kombinieren.</summary>
    IQueryable<ArchivableDocument> Query();

    /// <summary>Inhalt öffnen (Original); null, wenn nicht vorhanden.</summary>
    Task<Stream?> OpenAsync(Guid fileId, CancellationToken cancellationToken);
}
