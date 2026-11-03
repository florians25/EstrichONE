using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;

namespace EstrichOne.Api.Modules.Documentation;

internal sealed class GeneratedDocuments(FileService files, IAuditLog audit, TimeProvider clock, EstrichOneDbContext db, System.Threading.Channels.Channel<Guid> variants)
    : IGeneratedDocuments
{
    public async Task<Guid> StoreAsync(Guid bauakteId, Guid uploadedBy, string fileName, byte[] pdf, string category, string? folder, string? caption,
        CancellationToken cancellationToken)
    {
        var name = string.Join("-", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        var file = await files.StoreGeneratedAsync(bauakteId, uploadedBy, name, "application/pdf", pdf, category, folder, caption, clock.GetUtcNow(), cancellationToken);
        audit.Record("bauakte.file_added", uploadedBy, "bauakte", bauakteId.ToString(), new Dictionary<string, string?> { ["kind"] = "Document", ["generated"] = "true" });
        return file.Id;
    }

    public async Task<Guid> StoreLooseAsync(Guid uploadedBy, string fileName, byte[] pdf, CancellationToken cancellationToken)
    {
        var name = string.Join("-", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        var file = await files.StoreGeneratedAsync(null, uploadedBy, name, "application/pdf", pdf, null, null, null, clock.GetUtcNow(), cancellationToken);
        return file.Id;
    }

    public async Task<Guid> StoreAttachmentAsync(Guid bauakteId, Guid uploadedBy, string fileName, string contentType, byte[] content, bool photo,
        string? caption, CancellationToken cancellationToken)
    {
        var name = string.Join("-", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        var file = await files.StoreGeneratedAsync(bauakteId, uploadedBy, name.Length == 0 ? "chat-anhang" : name, contentType, content,
            photo ? null : "chat", null, caption, clock.GetUtcNow(), cancellationToken, photo ? FileKind.Photo : FileKind.Document);
        audit.Record("bauakte.file_added", uploadedBy, "bauakte", bauakteId.ToString(),
            new Dictionary<string, string?> { ["kind"] = file.Kind.ToString(), ["source"] = "chat" });
        await db.SaveChangesAsync(cancellationToken);
        if (file.Variants == VariantState.Pending)
        {
            variants.Writer.TryWrite(file.Id);
        }

        return file.Id;
    }
}
