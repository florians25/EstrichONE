using System.Text.Json;

namespace EstrichOne.Hosting.Operations;

/// <summary>Parameter eines Vorgangs (als JSON am Vorgang gespeichert).</summary>
public sealed record OperationParameters(
    string? Version = null,
    string? Reason = null,
    bool StopInstance = false,
    bool FinalBackup = false,
    bool BackupFirst = false)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static OperationParameters Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new OperationParameters() : JsonSerializer.Deserialize<OperationParameters>(json, Json) ?? new OperationParameters();
}
