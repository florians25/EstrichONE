using System.IO.Compression;
using System.Text;

namespace EstrichOne.Api.Modules.Identity.Passwords;

/// <summary>
/// Passwortregeln nach NIST SP 800-63B / BSI: Mindestlänge statt Zeichenklassen-Zwang, Abgleich mit häufigen
/// bzw. aus Datenlecks bekannten Passwörtern (offline, ohne externen Dienst), kein Zwangswechsel ohne Anlass.
/// Gilt beim Setzen und Ändern – bestehende Passwörter bleiben gültig, bis sie geändert werden.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
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
        var localPart = email?.Split('@')[0].ToLowerInvariant();
        if ((userName.Length >= 3 && lowered.Contains(userName.ToLowerInvariant(), StringComparison.Ordinal))
            || (localPart is { Length: >= 3 } && lowered.Contains(localPart, StringComparison.Ordinal)))
        {
            return "Das Passwort darf den Anmeldenamen nicht enthalten.";
        }

        if (password.Distinct().Count() < 5)
        {
            return "Das Passwort besteht aus zu wenigen verschiedenen Zeichen.";
        }

        if (IsRepetitionOrSequence(lowered))
        {
            return "Das Passwort besteht nur aus Wiederholungen oder einer Zeichenfolge (z. B. „abcabc…“, „123456…“).";
        }

        if (CommonPasswords.Contains(lowered))
        {
            return "Dieses Passwort ist sehr verbreitet bzw. aus Datenlecks bekannt und leicht zu erraten. Bitte ein anderes wählen.";
        }

        if (CommonPasswords.IsDecorated(lowered))
        {
            return "Das Passwort beruht auf einem sehr verbreiteten Wort oder Passwort mit angehängten Ziffern oder Zeichen "
                + "(z. B. „Passwort2026!“). Besser: mehrere zufällige Wörter.";
        }

        return null;
    }

    /// <summary>„abcabcabcabc“, „121212121212“, „123456789012“, „qwertzuiopü…“ – kurze Einheit wiederholt oder fortlaufend.</summary>
    private static bool IsRepetitionOrSequence(string lowered)
    {
        for (var unit = 1; unit <= lowered.Length / 2; unit++)
        {
            if (lowered.Length % unit == 0 && unit <= 4)
            {
                var part = lowered[..unit];
                if (string.Concat(Enumerable.Repeat(part, lowered.Length / unit)) == lowered)
                {
                    return true;
                }
            }
        }

        var ascending = true;
        var descending = true;
        for (var i = 1; i < lowered.Length; i++)
        {
            ascending &= lowered[i] - lowered[i - 1] == 1 || (lowered[i] == '0' && lowered[i - 1] == '9');
            descending &= lowered[i - 1] - lowered[i] == 1 || (lowered[i] == '9' && lowered[i - 1] == '0');
        }

        return ascending || descending;
    }
}

/// <summary>
/// Rund 49 000 häufige und aus Datenlecks bekannte Passwörter (Liste von zxcvbn/zxcvbn-ts, MIT-Lizenz) plus deutsche
/// Grundwörter (Betrieb, Monate, Vereine …). Eingebettet als <c>Assets/common-passwords.txt.gz</c>, kleingeschrieben.
/// </summary>
internal static class CommonPasswords
{
    private static readonly Lazy<HashSet<string>> Values = new(Load);

    public static int Count => Values.Value.Count;

    public static bool Contains(string lowered) => Values.Value.Contains(lowered) || Values.Value.Contains(Unleet(lowered));

    /// <summary>
    /// Verbreitetes Wort mit vorangestellten/angehängten Ziffern oder Sonderzeichen („Sommer2026!“, „!!Passwort123“)
    /// sowie Leetspeak („P@ssw0rd“).
    /// </summary>
    public static bool IsDecorated(string lowered)
    {
        var start = 0;
        var end = lowered.Length;
        while (start < end && !char.IsLetter(lowered[start]))
        {
            start++;
        }

        while (end > start && !char.IsLetter(lowered[end - 1]))
        {
            end--;
        }

        var core = lowered[start..end];
        if (core.Length < 4)
        {
            return core.Length == 0; // nur Ziffern/Zeichen ist ohnehin schwach, wenn es hier ankommt
        }

        var unleet = Unleet(core);
        if (Values.Value.Contains(core) || Values.Value.Contains(unleet))
        {
            return true;
        }

        // Wort doppelt („sommersommer“) bzw. mit Trennzeichen („sommer-sommer“).
        var letters = new string([.. unleet.Where(char.IsLetter)]);
        return letters.Length % 2 == 0 && letters.Length >= 8 && letters[..(letters.Length / 2)] == letters[(letters.Length / 2)..]
            && Values.Value.Contains(letters[..(letters.Length / 2)]);
    }

    private static string Unleet(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '@' or '4' => 'a',
                '3' => 'e',
                '1' or '!' => 'i',
                '0' => 'o',
                '$' or '5' => 's',
                '7' or '+' => 't',
                _ => c,
            });
        }

        return builder.ToString();
    }

    private static HashSet<string> Load()
    {
        using var stream = typeof(CommonPasswords).Assembly.GetManifestResourceStream("EstrichOne.Identity.common-passwords.txt.gz")
            ?? throw new InvalidOperationException("Passwortliste fehlt in der Assembly.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                set.Add(line);
            }
        }

        return set;
    }
}
