namespace EstrichOne.Api.Modules.Access.Exports;

/// <summary>Pflicht zur Zwei-Faktor-Anmeldung für Konten mit Verwaltungsrechten (Einstellungen → Sitzungen).</summary>
public interface ITwoFactorPolicy
{
    /// <summary>Wahr, wenn die Pflicht eingeschaltet ist und der Benutzer Verwaltungsrechte (<see cref="Permissions.Privileged"/>) hat.</summary>
    Task<bool> IsRequiredAsync(Guid userId, CancellationToken cancellationToken);
}
