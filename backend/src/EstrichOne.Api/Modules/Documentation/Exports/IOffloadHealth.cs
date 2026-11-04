namespace EstrichOne.Api.Modules.Documentation.Exports;

/// <param name="Configured">Storage gekoppelt.</param>
/// <param name="Reachable">Storage antwortet (Live-Abfrage).</param>
/// <param name="OffloadedFiles">Originale, die nur noch auf dem Storage liegen.</param>
/// <param name="Missing">Originale, die weder lokal noch auf dem Storage auffindbar sind.</param>
public sealed record OffloadHealthInfo(bool Configured, bool Reachable, string? StorageName, DateTimeOffset? LastContactAt, int OffloadedFiles, int Missing);

/// <summary>Zustand der Speicher-Auslagerung für das Status-Banner (gelb, nur für die Verwaltung).</summary>
public interface IOffloadHealth
{
    Task<OffloadHealthInfo> GetAsync(CancellationToken cancellationToken);
}
