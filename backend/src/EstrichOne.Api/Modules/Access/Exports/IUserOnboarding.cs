namespace EstrichOne.Api.Modules.Access.Exports;

/// <summary>Angaben aus dem Personalstamm für die Onboarding-Unterlagen.</summary>
/// <param name="DeviceQrPng">QR-Code (PNG) zum Einrichtungscode; der Browser erzeugt ihn wie in der Geräteverwaltung.</param>
public sealed record OnboardingDetails(
    string? JobTitle, IReadOnlyList<string> Crews, DateOnly? EntryDate, string? ContactName, string? ContactPhone, string? ContactEmail,
    string? DeviceCode, string? DeviceLink, DateTimeOffset? DeviceCodeExpiresAt, byte[]? DeviceQrPng);

/// <summary>Ergebnis der Kontoanlage; bei Fehlern <see cref="ErrorCode"/> (USER_EXISTS, UNKNOWN_ROLE).</summary>
public sealed record OnboardingAccount(Guid? UserId, string? UserName, string? Password, string? ErrorCode, string? Error);

/// <summary>
/// Benutzerkonto für neue Mitarbeiter anlegen und Onboarding-Unterlagen (Zugangsbrief nach DIN 5008, erweitert um
/// Willkommen, App-Einrichtung, erste Schritte, Ansprechpartner, Datenschutz) erzeugen bzw. per E-Mail senden.
/// </summary>
public interface IUserOnboarding
{
    string ServerUrl(HttpRequest request);

    /// <summary>Konto mit Einmalpasswort (Wechsel bei erster Anmeldung) und Rollen; speichert mit SaveChanges des Aufrufers.</summary>
    Task<OnboardingAccount> CreateAccountAsync(string displayName, string userName, string? email, IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken);

    /// <summary>Neues Einmalpasswort (beendet alle Sitzungen des Benutzers).</summary>
    Task<OnboardingAccount> NewPasswordAsync(Guid userId, CancellationToken cancellationToken);

    Task<byte[]> RenderAsync(Guid userId, string password, string serverUrl, OnboardingDetails details, CancellationToken cancellationToken);

    /// <summary>Unterlagen per E-Mail (Vorlage „Willkommen“), wenn der Benutzer eine Adresse hat und E-Mail eingerichtet ist.</summary>
    Task<bool> MailAsync(Guid userId, byte[] pdf, string serverUrl, CancellationToken cancellationToken);
}
