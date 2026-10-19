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
