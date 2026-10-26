using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Organization;

/// <summary>Eintrag einer Werteliste (B6): anlegen, umbenennen, sortieren, deaktivieren. Der Schlüssel bleibt fest.</summary>
public sealed class ValueListItem : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string List { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public string? ShortLabel { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private ValueListItem()
    {
    }

    public static ValueListItem Create(Guid id, string list, string key, string label, string? shortLabel, int sortOrder) =>
        new() { Id = id, List = list, Key = key, Label = label, ShortLabel = shortLabel, SortOrder = sortOrder };

    public void Update(string label, string? shortLabel, int sortOrder, bool isActive)
    {
        Label = label.Trim();
        ShortLabel = string.IsNullOrWhiteSpace(shortLabel) ? null : shortLabel.Trim();
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

internal sealed class ValueListItemConfiguration : IEntityTypeConfiguration<ValueListItem>
{
    public void Configure(EntityTypeBuilder<ValueListItem> builder)
    {
        builder.ToTable("value_list_items");
        builder.ConfigureVersioned();
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.List).HasMaxLength(40);
        builder.Property(e => e.Key).HasMaxLength(40);
        builder.Property(e => e.Label).HasMaxLength(200);
        builder.Property(e => e.ShortLabel).HasMaxLength(30);
        builder.HasIndex(e => new { e.List, e.Key }).IsUnique();
    }
}

public sealed record ValueListItemResponse(Guid Id, string List, string Key, string Label, string? ShortLabel, int SortOrder, bool IsActive, long Version);

public sealed record SaveValueListItemRequest(
    [property: Required, MaxLength(40)] string List,
    [property: Required, MaxLength(40)] string Key,
    [property: Required, MaxLength(200)] string Label,
    [property: MaxLength(30)] string? ShortLabel = null,
    int SortOrder = 0,
    bool IsActive = true);

internal sealed class ValueListStore(EstrichOneDbContext db) : IValueLists
{
    public Task<bool> ContainsAsync(string list, string key, CancellationToken cancellationToken) =>
        db.Set<ValueListItem>().AnyAsync(i => i.List == list && i.Key == key, cancellationToken);

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ValueListEntry>>> GetAllAsync(CancellationToken cancellationToken) =>
        (await db.Set<ValueListItem>().AsNoTracking().OrderBy(i => i.List).ThenBy(i => i.SortOrder).ThenBy(i => i.Label).ToListAsync(cancellationToken))
            .GroupBy(i => i.List)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ValueListEntry>)g.Select(i => new ValueListEntry(i.Key, i.Label, i.ShortLabel, i.SortOrder, i.IsActive)).ToList());
}

/// <summary>Grundbelegung je Liste (nur wenn die Liste noch leer ist); Schlüssel wie die bisherigen festen Werte.</summary>
internal sealed class ValueListSeed(EstrichOneDbContext db) : IDataInitializer
{
    public int Order => 25;

    internal static readonly Dictionary<string, (string Key, string Label, string? ShortLabel)[]> Defaults = new()
    {
        [ValueLists.ObjectType] = [("efh", "Einfamilienhaus", "EFH"), ("mfh", "Mehrfamilienhaus", "MFH"), ("commercial", "Gewerbe", null),
            ("industrial", "Industrie/Halle", null), ("public", "Öffentlicher Bau", null), ("renovation", "Sanierung", null), ("other", "Sonstiges", null)],
        [ValueLists.ScreedType] = [("ct", "Zementestrich (CT)", "CT"), ("caf", "Calciumsulfat-Fließestrich (CAF)", "CAF"), ("as", "Gussasphalt (AS)", "AS"),
            ("dry", "Trockenestrich", null), ("other", "Sonstiges", null)],
        [ValueLists.Construction] = [("bonded", "Verbundestrich", null), ("separatingLayer", "Auf Trennlage", null), ("floating", "Schwimmend", null),
            ("heated", "Heizestrich", null)],
        [ValueLists.StrengthClass] = [("C20-F3", "C20-F3", null), ("C25-F4", "C25-F4", null), ("C30-F5", "C30-F5", null), ("C35-F5", "C35-F5", null),
            ("CA-C25-F4", "CA-C25-F4", null), ("CA-C30-F5", "CA-C30-F5", null), ("IC10", "IC10", null), ("IC15", "IC15", null)],
        [ValueLists.Priority] = [("low", "Niedrig", null), ("normal", "Normal", null), ("high", "Hoch", null), ("urgent", "Dringend", null)],
        [ValueLists.HeatingType] = [("water", "Warmwasser-Fußbodenheizung", null), ("electric", "Elektrische Fußbodenheizung", null),
            ("cooling", "Fußbodenkühlung", null)],
        [ValueLists.Insulation] = [("eps", "EPS (Polystyrol)", null), ("xps", "XPS", null), ("pur", "PUR/PIR", null), ("mineral", "Mineralwolle", null),
            ("impact", "Trittschalldämmung", null)],
        [ValueLists.DocumentCategory] = [("offer", "Angebote", null), ("order", "Aufträge", null), ("plan", "Pläne", null), ("protocol", "Protokolle", null),
            ("acceptance", "Abnahmen", null), ("invoice", "Rechnungen", null), ("delivery", "Lieferscheine", null), ("customer", "Kundendokumente", null),
            ("office", "Büro", null), ("other", "Sonstiges", null)],
    };

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var present = await db.Set<ValueListItem>().Select(i => i.List).Distinct().ToListAsync(cancellationToken);
        var added = false;
        foreach (var (list, items) in Defaults.Where(d => !present.Contains(d.Key)))
        {
            for (var i = 0; i < items.Length; i++)
            {
                db.Add(ValueListItem.Create(Guid.CreateVersion7(), list, items[i].Key, items[i].Label, items[i].ShortLabel, (i + 1) * 10));
            }

            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

internal static partial class ValueListEndpoints
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,39}$")]
    private static partial Regex KeyPattern();

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/value-lists").WithTags("Stammdaten");
        group.MapGet("/", ListAsync).WithName("ListValueLists")
            .WithSummary("Wertelisten (Estricharten, Konstruktionen, Objektarten, Festigkeitsklassen, Dokumentarten, Prioritäten …)")
            .RequirePermission(Permissions.BauaktenRead);
        group.MapPut("/{id:guid}", SaveAsync).WithName("SaveValueListItem")
            .WithSummary("Eintrag anlegen oder ändern (Bezeichnung, Kurzform, Reihenfolge, aktiv); der Schlüssel ist nach dem Anlegen fest.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.SettingsManage);
    }

    internal static ValueListItemResponse ToResponse(ValueListItem i) => new(i.Id, i.List, i.Key, i.Label, i.ShortLabel, i.SortOrder, i.IsActive, i.Version);

    private static async Task<Ok<List<ValueListItemResponse>>> ListAsync(EstrichOneDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok((await db.Set<ValueListItem>().AsNoTracking().OrderBy(i => i.List).ThenBy(i => i.SortOrder).ThenBy(i => i.Label)
            .ToListAsync(cancellationToken)).Select(ToResponse).ToList());

    private static async Task<Results<Ok<ValueListItemResponse>, ProblemHttpResult>> SaveAsync(
        Guid id, SaveValueListItemRequest request, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (!KeyPattern().IsMatch(request.List) || !KeyPattern().IsMatch(request.Key))
        {
            return Problem("INVALID_KEY", "Liste und Schlüssel: Buchstaben, Ziffern, Punkt, Binde- oder Unterstrich (max. 40 Zeichen).");
        }

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return Problem("LABEL_REQUIRED", "Bitte eine Bezeichnung angeben.");
        }

        var item = await db.Set<ValueListItem>().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            if (await db.Set<ValueListItem>().AnyAsync(i => i.List == request.List && i.Key == request.Key, cancellationToken))
            {
                return Problem("DUPLICATE_KEY", $"Den Schlüssel „{request.Key}“ gibt es in dieser Liste schon.");
            }

            item = ValueListItem.Create(id, request.List, request.Key, request.Label.Trim(), null, request.SortOrder);
            db.Add(item);
        }
        else if (item.List != request.List || item.Key != request.Key)
        {
            return Problem("KEY_FIXED", "Liste und Schlüssel lassen sich nach dem Anlegen nicht ändern – bitte umbenennen oder deaktivieren.");
        }

        item.Update(request.Label, request.ShortLabel, request.SortOrder, request.IsActive);
        audit.Record("valueList.saved", currentUser.UserId, "valueListItem", item.Id.ToString(), new Dictionary<string, string?>
        {
            ["list"] = item.List, ["key"] = item.Key, ["label"] = item.Label, ["active"] = item.IsActive ? "ja" : "nein",
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(item));
    }

    private static ProblemHttpResult Problem(string code, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültige Angaben", detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>Wertelisten für die App (offline Auswahl, Formular-Schema).</summary>
internal sealed class ValueListItemSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "valueListItem";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<ValueListItem>().AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken))
            .ToDictionary(i => i.Id, i => SyncPayload.ToNode(ValueListEndpoints.ToResponse(i))!);
}
