namespace EstrichOne.Api.Modules.Organization.Exports;

public static class FormSchemaKeys
{
    public const string Bauakte = "bauakte";
    public const string Erstbegehung = "erstbegehung";
}

public sealed record FormFieldInfo(string Key, string Label, string Type, string? Unit, string? List);

/// <summary>Formular-Schemas der Stammdaten (Basisdaten, Erstbegehung).</summary>
public interface IFormSchemas
{
    /// <summary>Felder mit Zuordnung zu einem Basisdaten-Pfad: Feldschlüssel → Pfad (z. B. measuredArea → project.totalArea).</summary>
    Task<IReadOnlyDictionary<string, string>> TargetsAsync(string schemaKey, CancellationToken cancellationToken);

    /// <summary>Alle Felder in Anzeigereihenfolge (für Ausdrucke).</summary>
    Task<IReadOnlyList<FormFieldInfo>> FieldsAsync(string schemaKey, CancellationToken cancellationToken);
}
