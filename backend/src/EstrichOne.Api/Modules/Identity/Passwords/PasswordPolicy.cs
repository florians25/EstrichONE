namespace EstrichOne.Api.Modules.Identity.Passwords;

/// <summary>
/// Passwortregeln nach NIST SP 800-63B: Mindestlänge statt Zeichenklassen-Zwang, Abgleich mit häufigen
/// Passwörtern (offline, ohne externen Dienst), kein Zwangswechsel ohne Anlass.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    /// <summary>Liefert die verständliche Begründung, warum ein Passwort abgelehnt wird – oder <c>null</c>.</summary>
    public static string? Validate(string? password, string userName, string? email)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"Das Passwort muss mindestens {MinLength} Zeichen lang sein.";
        }

        if (password.Length > MaxLength)
        {
            return $"Das Passwort darf höchstens {MaxLength} Zeichen lang sein.";
        }

        var lowered = password.ToLowerInvariant();
        if (lowered.Contains(userName.ToLowerInvariant(), StringComparison.Ordinal)
            || (email is not null && lowered.Contains(email.Split('@')[0].ToLowerInvariant(), StringComparison.Ordinal)))
        {
            return "Das Passwort darf den Anmeldenamen nicht enthalten.";
        }

        if (password.Distinct().Count() < 4)
        {
            return "Das Passwort besteht aus zu wenigen verschiedenen Zeichen.";
        }

        if (CommonPasswords.Contains(lowered))
        {
            return "Dieses Passwort ist sehr verbreitet und leicht zu erraten. Bitte ein anderes wählen.";
        }

        return null;
    }
}

/// <summary>Häufig verwendete Passwörter (Auszug aus öffentlichen Listen, deutsch und international).</summary>
internal static class CommonPasswords
{
    private static readonly HashSet<string> Values = new(StringComparer.Ordinal)
    {
        "1234567890", "12345678910", "123456789a", "1q2w3e4r5t", "1qaz2wsx3edc", "qwertzuiop", "qwertyuiop",
        "asdfghjkl1", "passwort123", "passwort1234", "password123", "password1234", "password12", "passwort12",
        "hallo12345", "hallo123456", "schatz12345", "dennis12345", "michael123", "sommer2024", "sommer2025",
        "sommer2026", "winter2025", "winter2026", "fruehling2026", "herbst2026", "estrich123", "estrich2026",
        "estrichbau1", "baustelle1", "baustelle123", "willkommen1", "willkommen123", "geheim1234", "geheim12345",
        "iloveyou12", "fussball123", "fußball123", "bayernmuenchen", "borussia09", "schalke04!", "deutschland",
        "deutschland1", "computer123", "administrator", "admin12345", "admin123456", "letmein1234", "starwars123",
        "abc1234567", "abcdefghij", "1111111111", "0000000000", "1234512345", "9876543210", "0987654321",
        "qwerty1234", "qwertz1234", "trustno1234", "superman123", "batman1234", "dragon1234", "monkey1234",
        "football12", "baseball12", "princess12", "sunshine12", "master1234", "shadow1234", "passw0rd123",
        "p@ssw0rd12", "p@ssword123", "changeme123", "changeme1234", "test123456", "testtest12", "start12345",
        "anfang1234", "firma12345", "montag1234", "januar2026", "oktober2026", "zuhause123", "familie123",
    };

    public static bool Contains(string lowered) => Values.Contains(lowered);
}
