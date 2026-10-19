using EstrichOne.Api.Infrastructure.Persistence;

namespace EstrichOne.Api.Modules.Identity.Exports;

/// <summary>
/// Benutzerkonto. Anmeldedaten (Passwort-Hash, externe Identitäten) liegen in eigenen Tabellen des
/// Identity-Moduls; andere Module referenzieren nur die ID und den Anzeigenamen.
/// </summary>
public sealed class User : IVersionedEntity
{
    public const int MaxNameLength = 200;
    public const int MaxUserNameLength = 100;
    public const int MaxEmailLength = 320;

    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Anmeldename (für Kolonnenmitglieder ohne E-Mail-Adresse).</summary>
    public string UserName { get; private set; } = string.Empty;
    public string NormalizedUserName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? NormalizedEmail { get; private set; }
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? LastLoginAt { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private User()
    {
    }

    public static User Create(Guid id, string displayName, string userName, string? email = null, string? phone = null)
    {
        var user = new User { Id = id };
        user.Rename(displayName);
        user.ChangeUserName(userName);
        user.ChangeContact(email, phone);
        return user;
    }

    public void Rename(string displayName) => DisplayName = Required(displayName, nameof(displayName));

    public void ChangeUserName(string userName)
    {
        UserName = Required(userName, nameof(userName));
        NormalizedUserName = Normalize(UserName);
    }

    public void ChangeContact(string? email, string? phone)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        NormalizedEmail = Email is null ? null : Normalize(Email);
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void RecordLogin(DateTimeOffset at) => LastLoginAt = at;

    /// <summary>Vergleichsform für Anmeldename und E-Mail (Groß-/Kleinschreibung egal).</summary>
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Wert fehlt.", name) : value.Trim();
}
