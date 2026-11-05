using System.Text.Json;
using System.Text.Json.Nodes;

namespace EstrichOne.Api.Modules.Sync.Exports;

/// <summary>Eine Operation aus der Outbox eines Geräts (docs/sync-protokoll.md).</summary>
public sealed record SyncOperation(Guid OpId, string Type, Guid EntityId, long? BaseVersion, JsonObject Payload);

public enum SyncOperationStatus
{
    Applied,
    /// <summary>Bereits früher verarbeitet; Ergebnis von damals.</summary>
    Duplicate,
    /// <summary>Teilweise übernommen; mindestens ein Feld wurde parallel anders geändert.</summary>
    Conflict,
    Rejected,
}

public sealed record FieldConflict(string Field, JsonNode? ServerValue, JsonNode? ClientValue);

public sealed record SyncOperationResult(
    Guid OpId,
    SyncOperationStatus Status,
    string? Code = null,
    string? Message = null,
    long? Version = null,
    IReadOnlyList<FieldConflict>? Conflicts = null,
    JsonNode? Entity = null)
{
    public static SyncOperationResult Rejected(Guid opId, string code, string message) =>
        new(opId, SyncOperationStatus.Rejected, code, message);
}

/// <summary>Führt Operationen eines Typs aus (je Modul). Speichert selbst; die Transaktion stellt der Aufrufer.</summary>
public interface ISyncOperationHandler
{
    IReadOnlyCollection<string> OperationTypes { get; }

    Task<SyncOperationResult> ApplyAsync(SyncOperation operation, CancellationToken cancellationToken);
}

/// <summary>Lädt den aktuellen Stand von Entitäten eines Typs für den Änderungsabruf (inkl. Tombstones).</summary>
public interface ISyncEntitySource
{
    string EntityType { get; }

    /// <summary><c>null</c>: global, für alle mit dem Recht sichtbar. Sonst: die Sichtbarkeit folgt der Bauakte.</summary>
    string? RequiredPermission { get; }

    Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

/// <summary>Hilfen für die feldweise Zusammenführung (base/changes).</summary>
public static class SyncPayload
{
    public static JsonSerializerOptions Json { get; } = CreateOptions();

    public static JsonNode? ToNode<T>(T value) => JsonSerializer.SerializeToNode(value, Json);

    public static T? Read<T>(JsonObject payload, string name) =>
        payload.TryGetPropertyValue(name, out var node) && node is not null ? Value<T>(node, name) : default;

    /// <summary>
    /// Liest einen Feldwert. Nicht passende Werte (unbekannter Auswahlwert, Text statt Zahl, kein Datum …) lehnen
    /// genau diese Operation mit VALIDATION_FAILED ab, statt die ganze Anfrage scheitern zu lassen.
    /// </summary>
    public static T? Value<T>(JsonNode? node, string field)
    {
        try
        {
            return node.Deserialize<T>(Json);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            var path = (exception as JsonException)?.Path is { Length: > 1 } p && p[0] == '$' ? p[1..] : string.Empty;
            throw new SyncValidationException(InvalidValueMessage(typeof(T), field + path, node));
        }
    }

    internal static string InvalidValueMessage(Type type, string field, JsonNode? node)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        var shown = node is null ? "leer" : Shorten(node.ToJsonString());
        var expected = target switch
        {
            _ when target.IsEnum => "erlaubt: " + string.Join(", ", Enum.GetNames(target).Select(JsonNamingPolicy.CamelCase.ConvertName)),
            _ when target == typeof(string) => "erwartet: Text",
            _ when target == typeof(bool) => "erwartet: true oder false",
            _ when target == typeof(Guid) => "erwartet: UUID",
            _ when target == typeof(DateOnly) => "erwartet: Datum JJJJ-MM-TT",
            _ when target == typeof(TimeOnly) => "erwartet: Uhrzeit HH:MM",
            _ when target == typeof(DateTimeOffset) || target == typeof(DateTime) => "erwartet: Zeitpunkt nach ISO 8601",
            _ when target == typeof(int) || target == typeof(long) || target == typeof(decimal) || target == typeof(double) => "erwartet: Zahl",
            _ => null,
        };
        return expected is null
            ? $"Ungültiger Wert für „{field}“ ({shown})."
            : $"Ungültiger Wert für „{field}“ ({shown}); {expected}.";
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..57] + "…";

    /// <summary>
    /// Gleicht geänderte Felder ab: Ein Feld wird übernommen, wenn der Serverwert noch dem <c>base</c>-Wert des
    /// Clients entspricht (oder schon dem neuen Wert). Sonst entsteht für dieses Feld ein Konflikt; der Server behält
    /// seinen Wert.
    /// </summary>
    public static (List<string> Accepted, List<FieldConflict> Conflicts) Merge(
        JsonObject payload, IReadOnlyDictionary<string, JsonNode?> current, IReadOnlySet<string> allowedFields)
    {
        var changes = ObjectOrEmpty(payload, "changes");
        var bases = ObjectOrEmpty(payload, "base");
        var accepted = new List<string>();
        var conflicts = new List<FieldConflict>();

        foreach (var (field, newValue) in changes)
        {
            if (!allowedFields.Contains(field))
            {
                throw new SyncValidationException($"Das Feld „{field}“ kann nicht geändert werden.");
            }

            var serverValue = current.GetValueOrDefault(field);
            bases.TryGetPropertyValue(field, out var baseValue);
            if (JsonNode.DeepEquals(serverValue, baseValue) || JsonNode.DeepEquals(serverValue, newValue))
            {
                accepted.Add(field);
            }
            else
            {
                conflicts.Add(new FieldConflict(field, serverValue?.DeepClone(), newValue?.DeepClone()));
            }
        }

        return (accepted, conflicts);
    }

    private static JsonObject ObjectOrEmpty(JsonObject payload, string name) => payload[name] switch
    {
        null => new JsonObject(),
        JsonObject value => value,
        _ => throw new SyncValidationException($"„{name}“ muss ein Objekt mit Feldnamen und Werten sein."),
    };

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}

/// <summary>Ungültige Nutzdaten einer Operation (→ abgelehnt mit VALIDATION_FAILED).</summary>
public sealed class SyncValidationException(string message) : Exception(message);
