using System.Text.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

internal sealed class BauakteDataFill(EstrichOneDbContext db, IBauakteHistory history) : IBauakteDataFill
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task ApplyAsync(Guid bauakteId, IReadOnlyDictionary<string, JsonElement> values, string source, CancellationToken cancellationToken)
    {
        var bauakte = await db.Set<Bauakte>().SingleOrDefaultAsync(b => b.Id == bauakteId, cancellationToken);
        if (bauakte is null)
        {
            return;
        }

        var project = JsonSerializer.SerializeToNode(bauakte.Project, Json)!.AsObject();
        var custom = project["custom"] as JsonObject ?? [];
        var changed = new List<string>();
        DateOnly? start = bauakte.PlannedStart, end = bauakte.PlannedEnd;
        foreach (var (path, value) in values)
        {
            if (path.StartsWith("project.custom.", StringComparison.Ordinal))
            {
                custom[path["project.custom.".Length..]] = JsonNode.Parse(value.GetRawText());
            }
            else if (path.StartsWith("project.", StringComparison.Ordinal) && path.IndexOf('.', 8) < 0)
            {
                project[path["project.".Length..]] = JsonNode.Parse(value.GetRawText());
            }
            else if (path == "plannedStart" && value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out var s))
            {
                start = s;
            }
            else if (path == "plannedEnd" && value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out var e))
            {
                end = e;
            }
            else
            {
                continue;
            }

            changed.Add(path);
        }

        if (changed.Count == 0)
        {
            return;
        }

        project["custom"] = custom.Count > 0 ? custom : null;
        try
        {
            bauakte.ChangeProject(project.Deserialize<BauakteProject>(Json) ?? BauakteProject.Empty);
            if (start is null || end is null || end >= start)
            {
                bauakte.Schedule(start, end);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException)
        {
            // Unpassende Werte (z. B. Text in einem Zahlenfeld) übergehen – die Erstbegehung selbst bleibt gültig.
            return;
        }

        history.Record(bauakteId, BauakteHistoryActions.Updated, new Dictionary<string, string?>
        {
            ["fields"] = string.Join(',', changed), ["source"] = source,
        });
    }
}
