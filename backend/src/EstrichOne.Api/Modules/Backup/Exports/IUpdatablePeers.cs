namespace EstrichOne.Api.Modules.Backup.Exports;

/// <summary>
/// Ein gekoppelter Zusatzserver auf anderer Hardware, der per Fernaktualisierung auf die Version dieses Servers gebracht
/// werden kann (Backup-Ziel, Backup-Ziel, das zieht, Archivserver).
/// </summary>
/// <param name="Key">Stabil und URL-tauglich, z. B. <c>target-…</c>, <c>mirror-…</c>, <c>archive</c>.</param>
/// <param name="Kind"><c>backup-target</c>, <c>backup-mirror</c> oder <c>archive</c>.</param>
/// <param name="Role">Rolle des Zusatzservers im Auftrag: <c>backup-target</c> oder <c>archive</c>.</param>
/// <param name="ClientId">Kennung dieses Servers beim Zusatzserver (bzw. Kennung des Spiegels).</param>
/// <param name="TokenSha256">SHA-256 des Kopplungs-Tokens – Schlüssel für die Signatur des Auftrags.</param>
/// <param name="Connect">Verbindung mit gepinntem Zertifikat; <c>null</c> bei Zielen, die ziehen (die holen Aufträge selbst ab).</param>
/// <param name="ReportedVersion">Zuletzt bekannte Version (ohne Live-Abfrage).</param>
/// <param name="Link">Seite der Weboberfläche für Benachrichtigungen.</param>
public sealed record UpdatablePeer(string Key, string Kind, string Name, string Role, string ClientId, string TokenSha256,
    Func<TimeSpan, HttpClient>? Connect, string? ReportedVersion, string Link);

/// <summary>Module melden ihre Zusatzserver (Backup: Ziele und Spiegel; Archiv: Archivserver).</summary>
public interface IUpdatablePeerSource
{
    Task<IReadOnlyList<UpdatablePeer>> ListAsync(CancellationToken cancellationToken);
}
