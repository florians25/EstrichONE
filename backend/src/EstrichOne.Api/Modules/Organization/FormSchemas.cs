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
    bool Builtin = false,
    bool Photos = false,
    [property: MaxLength(80)] string? Target = null);

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
    public const string SchemaKey = FormSchemaKeys.Bauakte;

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

/// <summary>Erstbegehung: Prüfpunkte (check) und Messwerte; Felder mit Ziel ergänzen die Basisdaten.</summary>
internal static class InspectionFormFields
{
    public static readonly IReadOnlyDictionary<string, FormSchemaField> Builtin = new[]
    {
        new FormSchemaField("substrate", "Untergrund tragfähig, eben und sauber", "check", Photos: true, Builtin: true),
        new FormSchemaField("edgeInsulation", "Randdämmstreifen vorhanden", "check", Photos: true, Builtin: true),
        new FormSchemaField("heatingPressureTest", "Heizung: Druckprüfung erfolgt", "check", Photos: true, Builtin: true),
        new FormSchemaField("access", "Zufahrt und Pumpenstandort geklärt", "check", Builtin: true),
        new FormSchemaField("utilities", "Wasser und Strom vor Ort", "check", Builtin: true),
    }.ToDictionary(f => f.Key);

    public static IReadOnlyList<FormSchemaGroup> Default() =>
    [
        new("pruefpunkte", "Prüfpunkte", [.. Builtin.Values]),
        new("messwerte", "Messwerte und Aufbau", [
            new FormSchemaField("measuredArea", "Gemessene Fläche", "number", Unit: "m²", Target: "project.totalArea"),
            new FormSchemaField("screedType", "Estrichart", "select", List: ValueLists.ScreedType, Target: "project.screedType"),
            new FormSchemaField("thickness", "Estrichdicke", "integer", Unit: "mm", Target: "project.nominalThicknessMm"),
            new FormSchemaField("underfloorHeating", "Fußbodenheizung", "boolean", Target: "project.underfloorHeating"),
            new FormSchemaField("water", "Wasser vor Ort", "boolean", Target: "project.waterOnSite"),
            new FormSchemaField("power", "Strom vor Ort", "boolean", Target: "project.powerOnSite"),
        ]),
    ];
}

internal sealed class FormSchemaSeed(EstrichOneDbContext db) : IDataInitializer
{
    public int Order => 26;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var present = await db.Set<FormSchema>().Select(s => s.Key).ToListAsync(cancellationToken);
        if (!present.Contains(FormSchemaKeys.Bauakte))
        {
            db.Add(FormSchema.Create(Guid.CreateVersion7(), FormSchemaKeys.Bauakte, JsonSerializer.Serialize(BauakteFormFields.Default(), FormSchemaEndpoints.Json)));
        }

        if (!present.Contains(FormSchemaKeys.Erstbegehung))
        {
            db.Add(FormSchema.Create(Guid.CreateVersion7(), FormSchemaKeys.Erstbegehung, JsonSerializer.Serialize(InspectionFormFields.Default(), FormSchemaEndpoints.Json)));
        }

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
        if (key is not (FormSchemaKeys.Bauakte or FormSchemaKeys.Erstbegehung))
        {
            return Problem("UNKNOWN_FORM", "Unbekanntes Formular.");
        }

        var inspection = key == FormSchemaKeys.Erstbegehung;
        var builtins = inspection ? InspectionFormFields.Builtin : BauakteFormFields.Builtin;

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

                if (builtins.TryGetValue(f.Key, out var builtin))
                {
                    // Eingebaut: Typ, Ablage und Liste bleiben fest.
                    fields.Add(builtin with { Label = f.Label.Trim(), Required = f.Required, Help = f.Help, Badge = !inspection && f.Badge, Photos = f.Photos });
                    continue;
                }

                if (!(Types.Contains(f.Type) || (inspection && f.Type == "check")) || (f.Type == "select" && string.IsNullOrWhiteSpace(f.List)))
                {
                    return Problem("INVALID_FIELD", $"Feld „{f.Label}“: unbekannter Typ oder Auswahlfeld ohne Werteliste.");
                }

                if (inspection)
                {
                    var target = string.IsNullOrWhiteSpace(f.Target) || f.Type == "check" ? null : f.Target.Trim();
                    if (target is not null && !ValidTarget(target))
                    {
                        return Problem("INVALID_TARGET", $"Feld „{f.Label}“: unbekanntes Ziel in den Basisdaten ({target}).");
                    }

                    fields.Add(new FormSchemaField(f.Key, f.Label.Trim(), f.Type, null, f.Type == "select" ? f.List : null, f.Unit, f.Required, f.Help,
                        false, false, f.Photos, target));
                    continue;
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

    /// <summary>Ziel einer Erstbegehungs-Angabe: eingebautes Basisdaten-Feld oder eigenes Feld (project.custom.x).</summary>
    private static bool ValidTarget(string target) =>
        BauakteFormFields.Builtin.Values.Any(b => b.Path == target)
        || (target.StartsWith("project.custom.", StringComparison.Ordinal) && KeyPattern().IsMatch(target["project.custom.".Length..]));

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

internal sealed class FormSchemaStore(EstrichOneDbContext db) : IFormSchemas
{
    public async Task<IReadOnlyDictionary<string, string>> TargetsAsync(string schemaKey, CancellationToken cancellationToken)
    {
        var schema = await db.Set<FormSchema>().AsNoTracking().SingleOrDefaultAsync(s => s.Key == schemaKey, cancellationToken);
        return schema is null ? new Dictionary<string, string>()
            : FormSchemaEndpoints.Groups(schema).SelectMany(g => g.Fields).Where(f => f.Target is not null).ToDictionary(f => f.Key, f => f.Target!);
    }

    public async Task<IReadOnlyList<FormFieldInfo>> FieldsAsync(string schemaKey, CancellationToken cancellationToken)
    {
        var schema = await db.Set<FormSchema>().AsNoTracking().SingleOrDefaultAsync(s => s.Key == schemaKey, cancellationToken);
        return schema is null ? [] : [.. FormSchemaEndpoints.Groups(schema).SelectMany(g => g.Fields).Select(f => new FormFieldInfo(f.Key, f.Label, f.Type, f.Unit, f.List))];
    }
}
