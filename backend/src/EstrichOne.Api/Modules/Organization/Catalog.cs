using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Organization;

/// <summary>
/// Stammdaten-Kataloge des Betriebs: Textbausteine (Notizen, Tagesberichte, Protokolle), Ordnervorlagen der
/// Dokumentablage (je Dokumentart Unterordner) und Bauakten-Vorlagen (Beschreibung und Standardräume).
/// Inhalt ist je Art ein kleines JSON-Dokument; geprüft wird beim Speichern.
/// </summary>
public sealed class CatalogEntry : IVersionedEntity
{
    public const int MaxContentLength = 20_000;

    public Guid Id { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Content { get; private set; } = "{}";
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private CatalogEntry()
    {
    }

    public static CatalogEntry Create(Guid id, string kind) => new() { Id = id, Kind = kind };

    public void Update(string name, string content, int sortOrder, bool isActive)
    {
        Name = name.Trim();
        Content = content;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

public static class CatalogKinds
{
    public const string TextBlock = "textBlock";
    public const string FolderTemplate = "folderTemplate";
    public const string BauakteTemplate = "bauakteTemplate";
    public static readonly IReadOnlySet<string> All = new HashSet<string>([TextBlock, FolderTemplate, BauakteTemplate], StringComparer.Ordinal);
}

public sealed record CatalogEntryResponse(Guid Id, string Kind, string Name, JsonElement Content, int SortOrder, bool IsActive, long Version);

public sealed record SaveCatalogEntryRequest(
    [property: Required] string Kind,
    [property: Required, MaxLength(200)] string Name,
    [property: Required] JsonElement Content,
    int SortOrder = 0,
    bool IsActive = true,
    long? Version = null);

internal sealed class CatalogEntryConfiguration : IEntityTypeConfiguration<CatalogEntry>
{
    public void Configure(EntityTypeBuilder<CatalogEntry> builder)
    {
        builder.ToTable("catalog_entries");
        builder.ConfigureVersioned();
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasMaxLength(30);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Content).HasColumnType("jsonb");
        builder.HasIndex(e => new { e.Kind, e.SortOrder });
    }
}

internal static class CatalogEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var catalog = api.MapGroup("/catalog").WithTags("Stammdaten");
        catalog.MapGet("/", ListAsync).WithName("ListCatalog")
            .WithSummary("Stammdaten-Katalog: kind=textBlock|folderTemplate|bauakteTemplate; active=true nur aktive")
            .RequirePermission(Permissions.BauaktenRead);
        catalog.MapPut("/{id:guid}", SaveAsync).WithName("SaveCatalogEntry")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.SettingsManage);
        catalog.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteCatalogEntry")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.SettingsManage);
    }

    private static CatalogEntryResponse ToResponse(CatalogEntry e)
    {
        using var document = JsonDocument.Parse(e.Content);
        return new(e.Id, e.Kind, e.Name, document.RootElement.Clone(), e.SortOrder, e.IsActive, e.Version);
    }

    private static async Task<Ok<List<CatalogEntryResponse>>> ListAsync(EstrichOneDbContext db, CancellationToken cancellationToken,
        [FromQuery] string? kind = null, [FromQuery] bool active = false)
    {
        var query = db.Set<CatalogEntry>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(kind))
        {
            query = query.Where(e => e.Kind == kind);
        }

        if (active)
        {
            query = query.Where(e => e.IsActive);
        }

        var list = await query.OrderBy(e => e.Kind).ThenBy(e => e.SortOrder).ThenBy(e => e.Name).ToListAsync(cancellationToken);
        return TypedResults.Ok(list.Select(ToResponse).ToList());
    }

    private static async Task<Results<Ok<CatalogEntryResponse>, ProblemHttpResult>> SaveAsync(
        Guid id, SaveCatalogEntryRequest request, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (!CatalogKinds.All.Contains(request.Kind))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "UNKNOWN_KIND", "Unbekannte Stammdatenart.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "NAME_REQUIRED", "Bitte einen Namen angeben.");
        }

        var error = Validate(request.Kind, request.Content);
        if (error is not null)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "INVALID_CONTENT", error);
        }

        var content = request.Content.GetRawText();
        if (content.Length > CatalogEntry.MaxContentLength)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "CONTENT_TOO_LONG", "Der Inhalt ist zu lang.");
        }

        var entry = await db.Set<CatalogEntry>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entry is null)
        {
            entry = CatalogEntry.Create(id, request.Kind);
            db.Add(entry);
        }
        else if (request.Version is { } version && version != entry.Version)
        {
            return Problem(StatusCodes.Status412PreconditionFailed, "VERSION_CONFLICT", "Der Eintrag wurde inzwischen geändert. Bitte neu laden.");
        }
        else if (entry.Kind != request.Kind)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "KIND_CHANGED", "Die Art eines Eintrags lässt sich nicht ändern.");
        }

        entry.Update(request.Name, content, request.SortOrder, request.IsActive);
        audit.Record("catalog.saved", currentUser.UserId, "catalog", id.ToString(), new Dictionary<string, string?> { ["kind"] = request.Kind, ["name"] = entry.Name });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(entry));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var entry = await db.Set<CatalogEntry>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entry is null)
        {
            return Problem(StatusCodes.Status404NotFound, "NOT_FOUND", "Eintrag nicht gefunden");
        }

        db.Remove(entry);
        audit.Record("catalog.deleted", currentUser.UserId, "catalog", id.ToString(), new Dictionary<string, string?> { ["kind"] = entry.Kind, ["name"] = entry.Name });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// textBlock: { "text": "…", "usage": ["notes","diary","inspection"] }
    /// folderTemplate: { "category": "offer", "folders": ["Ausschreibung", …] }
    /// bauakteTemplate: { "description": "…", "rooms": [{ "name": "Wohnen", "floor": "EG", "thicknessMillimeters": 65, "underfloorHeating": true }] }
    /// </summary>
    private static string? Validate(string kind, JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
        {
            return "Der Inhalt muss ein Objekt sein.";
        }

        switch (kind)
        {
            case CatalogKinds.TextBlock:
                return content.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String && text.GetString()!.Trim().Length > 0
                    ? null : "Bitte den Text des Bausteins angeben.";
            case CatalogKinds.FolderTemplate:
                if (!content.TryGetProperty("category", out var category) || category.ValueKind != JsonValueKind.String)
                {
                    return "Bitte die Dokumentart angeben.";
                }

                return content.TryGetProperty("folders", out var folders) && folders.ValueKind == JsonValueKind.Array
                    && folders.EnumerateArray().All(f => f.ValueKind == JsonValueKind.String && f.GetString()!.Trim().Length is > 0 and <= 200)
                    ? null : "Bitte die Unterordner als Liste angeben.";
            case CatalogKinds.BauakteTemplate:
                if (content.TryGetProperty("rooms", out var rooms))
                {
                    if (rooms.ValueKind != JsonValueKind.Array || rooms.GetArrayLength() > 200)
                    {
                        return "Die Räume müssen eine Liste sein.";
                    }

                    if (rooms.EnumerateArray().Any(r => r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("name", out var n)
                            || n.ValueKind != JsonValueKind.String || n.GetString()!.Trim().Length == 0))
                    {
                        return "Jeder Raum braucht einen Namen.";
                    }
                }

                return null;
            default:
                return "Unbekannte Stammdatenart.";
        }
    }

    private static ProblemHttpResult Problem(int status, string code, string title) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
