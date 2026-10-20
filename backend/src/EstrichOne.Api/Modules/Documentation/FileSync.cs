using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>Dateien sind unveränderlich: Sync kennt nur Ankündigen und Löschen (docs/reengineering/15, Abschnitt 2).</summary>
internal sealed class FileSync(FileService files, EstrichOneDbContext db, IUserPermissions permissions, TimeProvider clock) : ISyncOperationHandler
{
    /// <summary>Nach der Ankündigung änderbar: nur Beschriftung und (bei Dokumenten) Dokumentart; der Inhalt bleibt unveränderlich.</summary>
    private static readonly HashSet<string> EditableFields = ["caption", "category"];

    public IReadOnlyCollection<string> OperationTypes { get; } = ["file.announce", "file.update", "file.delete"];

    public async Task<SyncOperationResult> ApplyAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (op.Type == "file.update")
        {
            return await UpdateAsync(op, cancellationToken);
        }

        if (op.Type == "file.delete")
        {
            if (!await permissions.HasAsync(Permissions.FilesDelete, cancellationToken))
            {
                return SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");
            }

            if (await files.FindReadableAsync(op.EntityId, cancellationToken) is null)
            {
                return SyncOperationResult.Rejected(op.OpId, "NOT_FOUND", "Die Datei existiert nicht (mehr) oder ist nicht sichtbar.");
            }

            var record = await db.Set<FileRecord>().SingleAsync(f => f.Id == op.EntityId, cancellationToken);
            record.Delete(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: record.Version);
        }

        if (!await permissions.HasAsync(Permissions.FilesUpload, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");
        }

        var p = op.Payload;
        var kind = SyncPayload.Read<FileKind>(p, "kind");
        var category = Category(SyncPayload.Read<string>(p, "category"), kind);
        var sha = SyncPayload.Read<string>(p, "sha256") ?? string.Empty;
        var size = SyncPayload.Read<long>(p, "sizeBytes");
        if (sha.Length != 64 || size <= 0)
        {
            throw new SyncValidationException("Größe und SHA-256 der Datei sind erforderlich.");
        }

        var (file, _, failure) = await files.AnnounceAsync(op.EntityId, SyncPayload.Read<Guid?>(p, "bauakteId"), kind,
            SyncPayload.Read<string>(p, "originalName") ?? "datei", SyncPayload.Read<string>(p, "contentType") ?? "application/octet-stream",
            size, sha, SyncPayload.Read<DateTimeOffset?>(p, "capturedAt"), SyncPayload.Read<double?>(p, "latitude"),
            SyncPayload.Read<double?>(p, "longitude"), SyncPayload.Read<string>(p, "caption"), cancellationToken);

        if (failure is null && category is not null && file!.Category is null)
        {
            file.Categorize(category);
            await db.SaveChangesAsync(cancellationToken);
        }

        return failure switch
        {
            null => new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: file!.Version, Entity: SyncPayload.ToNode(FileEndpoints.ToResponse(file))),
            FileFailure.BauakteNotFound => SyncOperationResult.Rejected(op.OpId, "BAUAKTE_NOT_FOUND", "Die Bauakte existiert nicht (mehr) oder ist nicht sichtbar."),
            FileFailure.TooLarge => SyncOperationResult.Rejected(op.OpId, "FILE_TOO_LARGE", "Die Datei ist zu groß."),
            _ => SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", "Unter dieser ID existiert bereits eine andere Datei."),
        };
    }

    private async Task<SyncOperationResult> UpdateAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.FilesUpload, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");
        }

        if (await files.FindReadableAsync(op.EntityId, cancellationToken) is null)
        {
            return SyncOperationResult.Rejected(op.OpId, "NOT_FOUND", "Die Datei existiert nicht (mehr) oder ist nicht sichtbar.");
        }

        var file = await db.Set<FileRecord>().SingleAsync(f => f.Id == op.EntityId, cancellationToken);
        var current = (JsonObject)SyncPayload.ToNode(FileEndpoints.ToResponse(file))!;
        var (accepted, conflicts) = SyncPayload.Merge(op.Payload, current.ToDictionary(pair => pair.Key, pair => pair.Value), EditableFields);
        var changes = (JsonObject)op.Payload["changes"]!;
        foreach (var field in accepted)
        {
            var value = changes[field]?.GetValue<string>();
            if (field == "caption")
            {
                var caption = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (caption is { Length: > 500 })
                {
                    throw new SyncValidationException("Die Beschriftung ist länger als 500 Zeichen.");
                }

                file.Describe(caption);
            }
            else
            {
                file.Categorize(Category(value, file.Kind));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, conflicts.Count == 0 ? SyncOperationStatus.Applied : SyncOperationStatus.Conflict,
            Version: file.Version, Conflicts: conflicts.Count == 0 ? null : conflicts, Entity: SyncPayload.ToNode(FileEndpoints.ToResponse(file)));
    }

    /// <summary>Dokumentart nur für Dokumente und nur aus der festen Liste.</summary>
    private static string? Category(string? category, FileKind kind)
    {
        if (string.IsNullOrWhiteSpace(category) || kind != FileKind.Document)
        {
            return null;
        }

        return DocumentCategories.All.Contains(category)
            ? category
            : throw new SyncValidationException($"Unbekannte Dokumentart „{category}“.");
    }
}

internal sealed class FileSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "file";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<FileRecord>().AsNoTracking().Where(f => ids.Contains(f.Id)).ToListAsync(cancellationToken))
            .ToDictionary(f => f.Id, f => SyncPayload.ToNode(FileEndpoints.ToResponse(f))!);
}
