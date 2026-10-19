using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Modules.Sync;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Sync;

internal static class SyncClient
{
    public static object Op(string type, Guid entityId, object payload, Guid? opId = null) => new
    {
        opId = opId ?? Guid.CreateVersion7(),
        type,
        entityId,
        baseVersion = (long?)null,
        payload,
    };

    public static async Task<PushResponse> PushAsync(this HttpClient client, params object[] operations)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/sync/push", new { operations }, TestJson.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PushResponse>(TestJson.Options))!;
    }

    public static async Task<PullResponse> PullAsync(this HttpClient client, string? cursor = null, bool includeScope = false)
    {
        var url = "/api/v1/sync/pull?limit=500&includeScope=" + includeScope + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
        return (await client.GetFromJsonAsync<PullResponse>(url, TestJson.Options))!;
    }

    /// <summary>
    /// Holt alle Änderungen ab Cursor, bis die Bedingung erfüllt ist. Der Feed liefert nur sicher abgeschlossene
    /// Transaktionen; parallel laufende Tests anderer Datenbanken können die Auslieferung kurz verzögern.
    /// </summary>
    public static async Task<(List<SyncChange> Changes, string Cursor)> PullUntilAsync(
        this HttpClient client, string? cursor, Func<List<SyncChange>, bool> done)
    {
        var all = new List<SyncChange>();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var page = await client.PullAsync(cursor);
            all.AddRange(page.Changes);
            cursor = page.Cursor;
            if (!page.HasMore && done(all))
            {
                return (all, cursor);
            }

            if (!page.HasMore)
            {
                await Task.Delay(100);
            }
        }

        throw new TimeoutException($"Erwartete Änderungen kamen nicht an. Erhalten: {string.Join(", ", all.Select(c => c.EntityType + ":" + c.Id))}");
    }

    public static string? Text(this JsonNode? node, string property) => node?[property]?.GetValue<string>();
}
