namespace EstrichOne.Api.Modules.Identity.Exports;

/// <summary>Benutzerkonten anlegen (Ersteinrichtung, Benutzerverwaltung).</summary>
public interface IUserAccounts
{
    /// <exception cref="PasswordRejectedException">Passwort verletzt die Passwortregeln.</exception>
    /// <exception cref="DuplicateUserException">Anmeldename oder E-Mail ist bereits vergeben.</exception>
    Task<User> CreateLocalUserAsync(string displayName, string userName, string? email, string password,
        bool mustChangePassword, CancellationToken cancellationToken);
}

public sealed class PasswordRejectedException(string reason) : Exception(reason);

public sealed class DuplicateUserException() : Exception("Anmeldename oder E-Mail-Adresse ist bereits vergeben.");
