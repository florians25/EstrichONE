namespace EstrichOne.Api.Modules.AppUpdates.Exports;

/// <summary>Versionsregeln für die Apps (Mindestversion je Plattform), z. B. für <c>server-info</c>.</summary>
public interface IAppUpdatePolicy
{
    /// <summary>Älteste Version, mit der eine App dieser Plattform (<c>android</c>, <c>ios</c>) noch arbeiten darf.</summary>
    Task<string> MinimumVersionAsync(string platform, CancellationToken cancellationToken);

    /// <summary>Stand einer installierten App gegenüber diesem Server (z. B. Gerätekontext im Helpdesk).</summary>
    Task<AppVersionState> EvaluateAsync(string platform, string? installedVersion, CancellationToken cancellationToken);
}

/// <param name="State">current, recommended, required, newerThanServer – oder null, wenn die Version unbekannt ist.</param>
public sealed record AppVersionState(string? State, string? LatestVersion, string MinimumVersion);
