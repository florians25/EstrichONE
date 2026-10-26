using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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

/// <summary>
/// Formular-Schema (B7): Gruppen und Felder der Basisdaten, im Web-Designer gebaut; gilt für Web-Assistent,
/// Basisdaten-Anzeige und App. Eingebaute Felder zeigen auf feste Bauakten-Felder, eigene auf <c>project.custom.&lt;key&gt;</c>.
/// </summary>
public sealed class FormSchema : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Definition { get; private set; } = "[]";
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private FormSchema()
    {
    }

    public static FormSchema Create(Guid id, string key, string definition) => new() { Id = id, Key = key, Definition = definition };

    public void Change(string definition) => Definition = definition;
}

internal sealed class FormSchemaConfiguration : IEntityTypeConfiguration<FormSchema>
{
    public void Configure(EntityTypeBuilder<FormSchema> builder)
    {
        builder.ToTable("form_schemas");
        builder.ConfigureVersioned();
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Key).HasMaxLength(40);
        builder.Property(e => e.Definition).HasColumnType("jsonb");
        builder.HasIndex(e => e.Key).IsUnique();
    }
}

public sealed record FormSchemaField(
    [property: Required, MaxLength(40)] string Key,
    [property: Required, MaxLength(100)] string Label,
    [property: Required, MaxLength(20)] string Type,
    string? Path = null,
    [property: MaxLength(40)] string? List = null,
    [property: MaxLength(20)] string? Unit = null,
    bool Required = false,
    [property: MaxLength(300)] string? Help = null,
    bool Badge = false,
    bool Builtin = false);

public sealed record FormSchemaGroup(
    [property: Required, MaxLength(40)] string Key,
    [property: Required, MaxLength(100)] string Label,
    IReadOnlyList<FormSchemaField> Fields);

public sealed record ValueListOption(string Key, string Label, string? ShortLabel);

public sealed record FormSchemaResponse(Guid Id, string Key, long Version, IReadOnlyList<FormSchemaGroup> Groups,
    IReadOnlyDictionary<string, IReadOnlyList<ValueListOption>>? Lists = null);

public sealed record SaveFormSchemaRequest([property: Required] IReadOnlyList<FormSchemaGroup> Groups);

/// <summary>Eingebaute Felder der Bauakte: Typ, Ablage und Liste sind fest; Beschriftung, Pflicht, Gruppe und Reihenfolge frei.</summary>
internal static class BauakteFormFields
{
    public const string SchemaKey = "bauakte";

    public static readonly IReadOnlyDictionary<string, FormSchemaField> Builtin = new[]
    {
        new FormSchemaField("objectType", "Objektart", "select", "project.objectType", ValueLists.ObjectType, Badge: true, Builtin: true),
        new FormSchemaField("floors", "Geschosse", "text", "project.floors", Help: "z. B. KG, EG, OG", Builtin: true),
        new FormSchemaField("totalArea", "Gesamtfläche", "number", "project.totalArea", Unit: "m²", Builtin: true),
        new FormSchemaField("priority", "Priorität", "select", "project.priority", ValueLists.Priority, Badge: true, Builtin: true),
        new FormSchemaField("plannedStart", "Ausführung von", "date", "plannedStart", Builtin: true),
        new FormSchemaField("plannedEnd", "Ausführung bis", "date", "plannedEnd", Builtin: true),
        new FormSchemaField("screedType", "Estrichart", "select", "project.screedType", ValueLists.ScreedType, Badge: true, Builtin: true),
        new FormSchemaField("construction", "Konstruktion", "select", "project.construction", ValueLists.Construction, Badge: true, Builtin: true),
        new FormSchemaField("nominalThicknessMm", "Nenndicke", "integer", "project.nominalThicknessMm", Unit: "mm", Badge: true, Builtin: true),
        new FormSchemaField("strengthClass", "Festigkeitsklasse", "select", "project.strengthClass", ValueLists.StrengthClass, Badge: true, Builtin: true),
        new FormSchemaField("underfloorHeating", "Fußbodenheizung", "boolean", "project.underfloorHeating", Badge: true, Builtin: true),
        new FormSchemaField("heatingType", "Art der Heizung", "select", "project.heatingType", ValueLists.HeatingType, Builtin: true),
        new FormSchemaField("insulation", "Dämmung", "text", "project.insulation", ValueLists.Insulation, Help: "z. B. EPS 035 DEO", Builtin: true),
        new FormSchemaField("insulationThicknessMm", "Dämmdicke", "integer", "project.insulationThicknessMm", Unit: "mm", Builtin: true),
        new FormSchemaField("siteAccess", "Zugang / Besonderheiten", "textarea", "project.siteAccess", Help: "Pumpenstandort, Zufahrt, Etage", Builtin: true),
        new FormSchemaField("waterOnSite", "Wasser vor Ort", "boolean", "project.waterOnSite", Builtin: true),
        new FormSchemaField("powerOnSite", "Strom vor Ort", "boolean", "project.powerOnSite", Builtin: true),
    }.ToDictionary(f => f.Key);

    private static readonly string[] ProjectKeys = ["objectType", "floors", "totalArea", "plannedStart", "plannedEnd", "priority"];
    private static readonly string[] ScreedKeys = ["screedType", "construction", "nominalThicknessMm", "strengthClass", "underfloorHeating", "heatingType",
        "insulation", "insulationThicknessMm"];
    private static readonly string[] SiteKeys = ["siteAccess", "waterOnSite", "powerOnSite"];

    public static IReadOnlyList<FormSchemaGroup> Default() =>
    [
        new("bauvorhaben", "Bauvorhaben", [.. ProjectKeys.Select(k => Builtin[k])]),
        new("estrich", "Estrich", [.. ScreedKeys.Select(k => Builtin[k])]),
        new("baustelle", "Baustelle", [.. SiteKeys.Select(k => Builtin[k])]),
    ];
}

internal sealed class FormSchemaSeed(EstrichOneDbContext db) : IDataInitializer
{
    public int Order => 26;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (await db.Set<FormSchema>().AnyAsync(s => s.Key == BauakteFormFields.SchemaKey, cancellationToken))
        {
            return;
        }

        db.Add(FormSchema.Create(Guid.CreateVersion7(), BauakteFormFields.SchemaKey,
            JsonSerializer.Serialize(BauakteFormFields.Default(), FormSchemaEndpoints.Json)));
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static partial class FormSchemaEndpoints
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Types = ["text", "textarea", "number", "integer", "select", "boolean", "date"];

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,39}$")]
    private static partial Regex KeyPattern();

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/forms").WithTags("Stammdaten");
        group.MapGet("/{key}", GetAsync).WithName("GetFormSchema")
            .WithSummary("Formular-Schema (z. B. „bauakte“: Basisdaten) mit den aktiven Wertelisten der Auswahlfelder")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.BauaktenRead);
        group.MapPut("/{key}", SaveAsync).WithName("SaveFormSchema")
            .WithSummary("Formular-Designer: Gruppen und Felder speichern (eingebaute Felder und eigene Felder)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.SettingsManage);
    }

    internal static IReadOnlyList<FormSchemaGroup> Groups(FormSchema schema) =>
        JsonSerializer.Deserialize<List<FormSchemaGroup>>(schema.Definition, Json) ?? [];

    internal static FormSchemaResponse ToResponse(FormSchema schema, IReadOnlyDictionary<string, IReadOnlyList<ValueListOption>>? lists = null) =>
        new(schema.Id, schema.Key, schema.Version, Groups(schema), lists);

    private static async Task<Results<Ok<FormSchemaResponse>, ProblemHttpResult>> GetAsync(
        string key, EstrichOneDbContext db, IValueLists valueLists, HttpContext http, CancellationToken cancellationToken)
    {
        var schema = await db.Set<FormSchema>().AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (schema is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Formular nicht gefunden");
        }

        var groups = Groups(schema);
        var used = groups.SelectMany(g => g.Fields).Select(f => f.List).OfType<string>().ToHashSet();
        var lists = (await valueLists.GetAllAsync(cancellationToken))
            .Where(l => used.Contains(l.Key))
            .ToDictionary(l => l.Key, l => (IReadOnlyList<ValueListOption>)l.Value.Where(e => e.IsActive).Select(e => new ValueListOption(e.Key, e.Label, e.ShortLabel)).ToList());
        http.Response.Headers.ETag = $"\"{schema.Version}\"";
        return TypedResults.Ok(new FormSchemaResponse(schema.Id, schema.Key, schema.Version, groups, lists));
    }

    private static async Task<Results<Ok<FormSchemaResponse>, ProblemHttpResult>> SaveAsync(
        string key, SaveFormSchemaRequest request, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (key != BauakteFormFields.SchemaKey)
        {
            return Problem("UNKNOWN_FORM", "Unbekanntes Formular.");
        }

        var groups = new List<FormSchemaGroup>();
        var seen = new HashSet<string>();
        foreach (var g in request.Groups)
        {
            if (!KeyPattern().IsMatch(g.Key) || string.IsNullOrWhiteSpace(g.Label))
            {
                return Problem("INVALID_GROUP", "Jede Gruppe braucht einen Schlüssel und eine Bezeichnung.");
            }

            var fields = new List<FormSchemaField>();
            foreach (var f in g.Fields)
            {
                if (!KeyPattern().IsMatch(f.Key) || !seen.Add(f.Key) || string.IsNullOrWhiteSpace(f.Label))
                {
                    return Problem("INVALID_FIELD", $"Feld „{f.Key}“: Schlüssel fehlt, ist ungültig oder doppelt.");
                }

                if (BauakteFormFields.Builtin.TryGetValue(f.Key, out var builtin))
                {
                    // Eingebaut: Typ, Ablage und Liste bleiben fest.
                    fields.Add(builtin with { Label = f.Label.Trim(), Required = f.Required, Help = f.Help, Badge = f.Badge });
                    continue;
                }

                if (!Types.Contains(f.Type) || (f.Type == "select" && string.IsNullOrWhiteSpace(f.List)))
                {
                    return Problem("INVALID_FIELD", $"Feld „{f.Label}“: unbekannter Typ oder Auswahlfeld ohne Werteliste.");
                }

                fields.Add(new FormSchemaField(f.Key, f.Label.Trim(), f.Type, $"project.custom.{f.Key}", f.Type == "select" ? f.List : null,
                    f.Unit, f.Required, f.Help, f.Badge, false));
            }

            groups.Add(new FormSchemaGroup(g.Key, g.Label.Trim(), fields));
        }

        var schema = await db.Set<FormSchema>().SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        var definition = JsonSerializer.Serialize(groups, Json);
        if (schema is null)
        {
            schema = FormSchema.Create(Guid.CreateVersion7(), key, definition);
            db.Add(schema);
        }
        else
        {
            schema.Change(definition);
        }

        audit.Record("formSchema.saved", currentUser.UserId, "formSchema", key,
            new Dictionary<string, string?> { ["fields"] = string.Join(',', seen) });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(schema));
    }

    private static ProblemHttpResult Problem(string code, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültiges Formular", detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>Formular-Schemas für die App.</summary>
internal sealed class FormSchemaSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "formSchema";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<FormSchema>().AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(cancellationToken))
            .ToDictionary(s => s.Id, s => SyncPayload.ToNode(FormSchemaEndpoints.ToResponse(s))!);
}
