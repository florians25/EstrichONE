using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>
/// Prüft Diagnosedaten gegen eine feste Liste erlaubter Felder (docs/reengineering/14, Abschnitt 3). Unbekannte
/// Felder werden abgelehnt, nicht stillschweigend gespeichert – so gelangen keine Inhalte über neue Felder hinein.
/// Protokollzeilen sind auf einen engen Zeichenvorrat beschränkt. Dass gar keine Inhalte entstehen, stellt der
/// Logger der Apps sicher (er hat keine Schnittstelle für Fachinhalte); die Prüfung hier ist die zweite Linie.
/// </summary>
public static partial class TicketDiagnostics
{
    public const int MaxBytes = 64 * 1024;

    private static readonly HashSet<string> TextFields =
    [
        "appVersion", "build", "platform", "osVersion", "deviceModel", "deviceKind", "deviceId", "network",
        "syncOldestPending", "syncLastSuccess",
    ];

    private static readonly HashSet<string> NumberFields =
        ["syncPendingOperations", "syncPendingUploads", "syncFailedUploads", "syncPendingBytes", "syncConflicts", "freeStorageBytes"];

    /// <summary>Liefert das bereinigte JSON oder eine verständliche Fehlermeldung.</summary>
    public static (string? Json, string? Error) Validate(JsonNode? node)
    {
        if (node is null)
        {
            return (null, null);
        }

        if (node is not JsonObject data)
        {
            return (null, "Die Diagnosedaten haben ein ungültiges Format.");
        }

        foreach (var (key, value) in data)
        {
            var valid = key switch
            {
                _ when TextFields.Contains(key) => value is JsonValue text && text.TryGetValue<string>(out var s) && s.Length <= 200,
                _ when NumberFields.Contains(key) => value is JsonValue number && number.TryGetValue<double>(out var n) && n >= 0,
                "errorCodes" => value is JsonArray codes && codes.Count <= 50 && codes.All(IsErrorCode),
                "log" => value is JsonArray lines && lines.Count <= 500 && lines.All(line =>
                    line is JsonValue v && v.TryGetValue<string>(out var s) && s.Length <= 200 && LogLine().IsMatch(s)),
                _ => false,
            };
            if (!valid)
            {
                return (null, $"Das Diagnosefeld „{key}“ ist nicht erlaubt oder ungültig.");
            }
        }

        var json = data.ToJsonString();
        return json.Length > MaxBytes ? (null, "Die Diagnosedaten sind zu groß.") : (json, null);
    }

    private static bool IsErrorCode(JsonNode? node) =>
        node is JsonObject entry && entry.Count <= 2
        && entry["code"] is JsonValue code && code.TryGetValue<string>(out var c) && ErrorCode().IsMatch(c)
        && (entry["at"] is null || (entry["at"] is JsonValue at && at.TryGetValue<string>(out var a) && a.Length <= 40 && LogLine().IsMatch(a)));

    /// <summary>Großbuchstaben-Codes wie <c>SYNC_UPLOAD_FAILED</c>.</summary>
    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,63}$")]
    private static partial Regex ErrorCode();

    /// <summary>Zeitstempel, Codes, Zahlen, gekürzte IDs und einfache Trenner (keine Umlaute, Anführungszeichen, @ usw.).</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_:.\-+=/ ]*$")]
    private static partial Regex LogLine();

    public static JsonNode? Parse(string? json) => json is null ? null : JsonNode.Parse(json, documentOptions: new JsonDocumentOptions());
}
