using System.Text.Json;

namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>Basisdaten der Bauakte aus anderen Erfassungen ergänzen (z. B. Erstbegehung: gemessene Fläche → project.totalArea).</summary>
public interface IBauakteDataFill
{
    /// <summary>Pfade: project.&lt;feld&gt;, project.custom.&lt;feld&gt;, plannedStart, plannedEnd. Speichert mit dem nächsten SaveChanges.</summary>
    Task ApplyAsync(Guid bauakteId, IReadOnlyDictionary<string, JsonElement> values, string source, CancellationToken cancellationToken);
}
