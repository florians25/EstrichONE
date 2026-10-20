using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EstrichOne.Hosting.Domain;

/// <summary>
/// Strenge Prüfregeln für alles, was in Dateinamen, Compose-Projektnamen, Containernamen, .env-Dateien oder der
/// Proxy-Konfiguration landet. Docker wird ohne Shell mit Argumentlisten aufgerufen – die Regeln sind die zweite Linie.
/// </summary>
public static partial class Validation
{
    /// <summary>Reservierte Kürzel (Konsole, Proxy, übliche Infrastruktur-Namen).</summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.Ordinal)
    {
        "admin", "api", "console", "konsole", "hosting", "proxy", "www", "mail", "smtp", "imap", "ftp", "status",
        "docker", "root", "test", "localhost", "intern", "internal", "support", "hilfe", "help", "static", "cdn",
    };

    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 32;

    [GeneratedRegex("^[a-z][a-z0-9-]*[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    [GeneratedRegex("^(?!-)[a-z0-9-]{1,63}(?<!-)$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelPattern();

    [GeneratedRegex("^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\\.[A-Za-z0-9-]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[a-z][a-z0-9._-]{2,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserNamePattern();

    /// <summary>Kürzel: 3–32 Zeichen, a–z, 0–9 und einzelne Bindestriche, beginnt mit Buchstabe.</summary>
    public static string? CheckSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug))
        {
            return "Das Kürzel fehlt.";
        }

        if (slug.Length < SlugMinLength || slug.Length > SlugMaxLength)
        {
            return $"Das Kürzel muss {SlugMinLength} bis {SlugMaxLength} Zeichen lang sein.";
        }

        if (!SlugPattern().IsMatch(slug) || slug.Contains("--", StringComparison.Ordinal))
        {
            return "Das Kürzel darf nur Kleinbuchstaben, Ziffern und einzelne Bindestriche enthalten und muss mit einem Buchstaben beginnen.";
        }

        return ReservedSlugs.Contains(slug) ? $"Das Kürzel „{slug}“ ist reserviert." : null;
    }

    public static bool IsValidSlug(string? slug) => CheckSlug(slug) is null;

    /// <summary>Vollständiger Hostname (mind. zwei Labels, keine IP-Adresse, kein Platzhalter, ASCII/Punycode).</summary>
    public static string? CheckDomain(string? domain)
    {
        if (string.IsNullOrEmpty(domain))
        {
            return "Die Domain fehlt.";
        }

        if (domain.Length > 253)
        {
            return "Die Domain ist zu lang.";
        }

        var labels = domain.Split('.');
        if (labels.Length < 2 && domain != "localhost")
        {
            return "Bitte eine vollständige Domain angeben, z. B. firma.estrichone-hosting.de.";
        }

        if (labels.Any(label => !LabelPattern().IsMatch(label)))
        {
            return "Die Domain enthält unzulässige Zeichen (erlaubt: a–z, 0–9, Bindestrich; Umlaute als Punycode xn--…).";
        }

        if (labels.All(label => label.All(char.IsAsciiDigit)) || labels[^1].All(char.IsAsciiDigit))
        {
            return "Bitte eine Domain statt einer IP-Adresse angeben.";
        }

        return null;
    }

    /// <summary>Wandelt eine eingegebene Domain in die kanonische Form (klein, ohne Punkt am Ende, Umlaute als Punycode).</summary>
    public static string NormalizeDomain(string? domain)
    {
        var value = (domain ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        if (value.StartsWith("https://", StringComparison.Ordinal))
        {
            value = value["https://".Length..];
        }
        else if (value.StartsWith("http://", StringComparison.Ordinal))
        {
            value = value["http://".Length..];
        }

        value = value.TrimEnd('/');
        try
        {
            return value.Length == 0 ? value : new IdnMapping().GetAscii(value);
        }
        catch (ArgumentException)
        {
            return value;
        }
    }

    public static string? CheckEmail(string? email, bool required)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return required ? "Die E-Mail-Adresse fehlt." : null;
        }

        return email.Length <= 254 && EmailPattern().IsMatch(email) ? null : "Die E-Mail-Adresse ist ungültig.";
    }

    /// <summary>
    /// Freitext für Anzeige und .env (Firmenname): keine Steuerzeichen und keine Zeichen, die in einer .env-Datei
    /// Bedeutung haben (Anführungszeichen, $, Backslash, Backtick).
    /// </summary>
    public static string? CheckText(string? value, string field, int maxLength, bool required, bool envSafe = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return required ? $"{field} fehlt." : null;
        }

        if (value.Length > maxLength)
        {
            return $"{field} ist zu lang (höchstens {maxLength} Zeichen).";
        }

        if (value.Any(char.IsControl))
        {
            return $"{field} enthält Steuerzeichen.";
        }

        if (envSafe && value.IndexOfAny(['"', '$', '\\', '`']) >= 0)
        {
            return $"{field} darf keine Anführungszeichen, $, \\ oder ` enthalten.";
        }

        return null;
    }

    public static string? CheckVersion(string? version) =>
        !string.IsNullOrEmpty(version) && version.Length <= 64 && VersionPattern().IsMatch(version)
            ? null
            : "Die Version muss die Form 1.2.3 oder 1.2.3-preview.4 haben.";

    public static string? CheckUserName(string? userName) =>
        userName is not null && UserNamePattern().IsMatch(userName)
            ? null
            : "Der Benutzername muss 3–32 Zeichen lang sein (a–z, 0–9, Punkt, Bindestrich, Unterstrich) und mit einem Buchstaben beginnen.";

    public static string? CheckPassword(string? password) =>
        password is { Length: >= 12 and <= 256 } ? null : "Das Passwort muss mindestens 12 Zeichen lang sein.";

    /// <summary>Schlägt aus dem Firmennamen ein gültiges Kürzel vor („Estrichbau Müller GmbH“ → „estrichbau-mueller“).</summary>
    public static string SuggestSlug(string? companyName)
    {
        var text = (companyName ?? string.Empty).ToLowerInvariant()
            .Replace("ä", "ae", StringComparison.Ordinal).Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal).Replace("ß", "ss", StringComparison.Ordinal);
        foreach (var suffix in new[] { " gmbh & co. kg", " gmbh & co kg", " gmbh", " ug", " ag", " kg", " ohg", " gbr", " e.k.", " ek", " mbh" })
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                text = text[..^suffix.Length];
            }
        }

        var builder = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
            {
                builder.Append(c);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > SlugMaxLength)
        {
            slug = slug[..SlugMaxLength].TrimEnd('-');
        }

        if (slug.Length > 0 && !char.IsAsciiLetterLower(slug[0]))
        {
            slug = ("m-" + slug)[..Math.Min(SlugMaxLength, slug.Length + 2)].TrimEnd('-');
        }

        while (slug.Length < SlugMinLength)
        {
            slug += "x";
        }

        return ReservedSlugs.Contains(slug) ? slug + "-1" : slug;
    }
}
