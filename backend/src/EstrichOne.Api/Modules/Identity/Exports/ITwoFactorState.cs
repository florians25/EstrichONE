namespace EstrichOne.Api.Modules.Identity.Exports;

/// <summary>Zwei-Faktor-Anmeldung (TOTP) – Zustand für andere Module (Rechteprüfung, Benutzerverwaltung).</summary>
public interface ITwoFactorState
{
    Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> EnabledUsersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Administrator setzt die Zwei-Faktor-Anmeldung zurück (Handy verloren). Beendet alle Sitzungen des Benutzers.
    /// Gespeichert wird mit dem nächsten <c>SaveChangesAsync</c> des Aufrufers.
    /// </summary>
    Task<bool> ResetAsync(Guid userId, CancellationToken cancellationToken);
}
