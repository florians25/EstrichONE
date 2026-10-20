namespace EstrichOne.Api.Modules.Identity.Exports;

/// <summary>Benutzerkonten anlegen (Ersteinrichtung, Benutzerverwaltung).</summary>
public interface IUserAccounts
{
    /// <exception cref="PasswordRejectedException">Passwort verletzt die Passwortregeln.</exception>
    /// <exception cref="DuplicateUserException">Anmeldename oder E-Mail ist bereits vergeben.</exception>
    Task<User> CreateLocalUserAsync(string displayName, string userName, string? email, string password,
        bool mustChangePassword, CancellationToken cancellationToken);

    /// <summary>
    /// Setzt ein Einmalpasswort (Wechsel bei der nächsten Anmeldung erzwungen) und beendet alle Sitzungen.
    /// Speichert noch nicht.
    /// </summary>
    Task<string> ResetPasswordAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Sperrt bzw. entsperrt ein Konto; Sperren beendet alle Sitzungen sofort. Speichert noch nicht.</summary>
    Task SetActiveAsync(Guid userId, bool active, CancellationToken cancellationToken);

    /// <summary>Zufälliges, gut lesbares Einmalpasswort (ohne verwechselbare Zeichen).</summary>
    string GenerateOneTimePassword();
}

public sealed class PasswordRejectedException(string reason) : Exception(reason);

public sealed class DuplicateUserException() : Exception("Anmeldename oder E-Mail-Adresse ist bereits vergeben.");
