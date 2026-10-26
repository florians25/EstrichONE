namespace EstrichOne.Api.Modules.Organization.Exports;

/// <summary>Ein Eintrag einer Werteliste (Stammdaten B6), z. B. Estrichart „ct“ → „Zementestrich (CT)“.</summary>
public sealed record ValueListEntry(string Key, string Label, string? ShortLabel, int SortOrder, bool IsActive);

/// <summary>Wertelisten der Stammdaten; Auswahlfelder werden gegen diese Listen geprüft statt gegen feste Enums.</summary>
public interface IValueLists
{
    /// <summary>Ist <paramref name="key"/> in der Liste vorhanden (aktiv oder deaktiviert – Bestandswerte bleiben gültig)?</summary>
    Task<bool> ContainsAsync(string list, string key, CancellationToken cancellationToken);

    /// <summary>Alle Listen mit ihren Einträgen, sortiert.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<ValueListEntry>>> GetAllAsync(CancellationToken cancellationToken);
}

/// <summary>Bekannte Listen; eigene Listen sind zusätzlich möglich.</summary>
public static class ValueLists
{
    public const string ObjectType = "objectType";
    public const string ScreedType = "screedType";
    public const string Construction = "construction";
    public const string StrengthClass = "strengthClass";
    public const string Priority = "priority";
    public const string HeatingType = "heatingType";
    public const string Insulation = "insulation";
    public const string DocumentCategory = "documentCategory";
}
