namespace EstrichOne.Api.Modules.AppUpdates.Exports;

/// <summary>Versionsregeln für die Apps (Mindestversion je Plattform), z. B. für <c>server-info</c>.</summary>
public interface IAppUpdatePolicy
{
    /// <summary>Älteste Version, mit der eine App dieser Plattform (<c>android</c>, <c>ios</c>) noch arbeiten darf.</summary>
    Task<string> MinimumVersionAsync(string platform, CancellationToken cancellationToken);
}
