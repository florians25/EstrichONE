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
        payload.TryGetPropertyValue(name, out var node) && node is not null ? node.Deserialize<T>(Json) : default;

    /// <summary>
    /// Gleicht geänderte Felder ab: Ein Feld wird übernommen, wenn der Serverwert noch dem <c>base</c>-Wert des
    /// Clients entspricht (oder schon dem neuen Wert). Sonst entsteht für dieses Feld ein Konflikt; der Server behält
    /// seinen Wert.
    /// </summary>
    public static (List<string> Accepted, List<FieldConflict> Conflicts) Merge(
        JsonObject payload, IReadOnlyDictionary<string, JsonNode?> current, IReadOnlySet<string> allowedFields)
    {
        var changes = payload["changes"] as JsonObject ?? new JsonObject();
        var bases = payload["base"] as JsonObject ?? new JsonObject();
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

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

/// <summary>Ungültige Nutzdaten einer Operation (→ abgelehnt mit VALIDATION_FAILED).</summary>
public sealed class SyncValidationException(string message) : Exception(message);
