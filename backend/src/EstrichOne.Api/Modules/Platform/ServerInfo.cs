namespace EstrichOne.Api.Modules.Platform;

/// <summary>
/// Öffentliche Serverbeschreibung für den Einrichtungsassistenten der Apps
/// (siehe docs/reengineering/10-server-onboarding.md). Enthält keine vertraulichen Angaben.
/// </summary>
public sealed record ServerInfo(
    string Product,
    string ServerName,
    string ServerVersion,
    IReadOnlyList<string> ApiVersions,
    string MinClientVersion,
    IReadOnlyList<string> AuthMethods,
    Guid InstanceEpoch,
    bool SetupRequired)
{
    public const string ProductName = "EstrichONE";
}

/// <param name="ConfiguredUrl">Eingestellte öffentliche Adresse (EstrichOne:PublicUrl); null, wenn nicht gesetzt.</param>
/// <param name="RequestUrl">Adresse, unter der diese Anfrage ankam (Schema laut X-Forwarded-Proto, Host vom Proxy).</param>
/// <param name="Matches">Host stimmt überein (oder es ist keine bzw. nur localhost eingestellt).</param>
/// <param name="Https">Die Anfrage kam verschlüsselt an (direkt oder über den Proxy).</param>
public sealed record AddressCheck(string? ConfiguredUrl, string RequestUrl, bool Matches, bool Https);
