using System.Globalization;

namespace EstrichOne.Api.Infrastructure.Hosting;

/// <summary>
/// Versionsvergleich nach Semantic Versioning (0.1.0-preview.9 &lt; 0.1.0-preview.10 &lt; 0.1.0 &lt; 0.1.1), wie update.sh.
/// Gilt für Server- und App-Versionen; ein vorangestelltes „v“ und Build-Metadaten („+…“) werden ignoriert.
/// </summary>
public static class VersionComparer
{
    public static int Compare(string a, string b)
    {
        var (ca, pa) = Split(a);
        var (cb, pb) = Split(b);
        for (var i = 0; i < 3; i++)
        {
            if (ca[i] != cb[i])
            {
                return ca[i].CompareTo(cb[i]);
            }
        }

        if (pa.Length == 0 || pb.Length == 0)
        {
            // Ohne Vorabkennung ist die Version neuer als jede Vorabversion derselben Nummer.
            return pa.Length == pb.Length ? 0 : pa.Length == 0 ? 1 : -1;
        }

        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var na = int.TryParse(pa[i], NumberStyles.None, CultureInfo.InvariantCulture, out var ia);
            var nb = int.TryParse(pb[i], NumberStyles.None, CultureInfo.InvariantCulture, out var ib);
            var result = na && nb ? ia.CompareTo(ib) : na != nb ? (na ? -1 : 1) : string.CompareOrdinal(pa[i], pb[i]);
            if (result != 0)
            {
                return Math.Sign(result);
            }
        }

        return Math.Sign(pa.Length.CompareTo(pb.Length));
    }

    /// <summary>Gültige Versionsnummer (1 bis 3 Zahlen, optional mit Vorabkennung), z. B. <c>1.0.5</c> oder <c>1.1.0-preview.2</c>.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 50)
        {
            return false;
        }

        var text = value.Trim().TrimStart('v').Split('+')[0];
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var core = (dash < 0 ? text : text[..dash]).Split('.');
        if (core.Length is < 1 or > 3 || core.Any(part => part.Length == 0 || part.Length > 6 || !part.All(char.IsAsciiDigit)))
        {
            return false;
        }

        return dash < 0 || text[(dash + 1)..].Split('.').All(part => part.Length > 0 && part.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    /// <summary>Normalform ohne „v“ und Build-Metadaten, z. B. <c>v1.0.5+abc</c> → <c>1.0.5</c>.</summary>
    public static string Normalize(string value) => value.Trim().TrimStart('v').Split('+')[0];

    /// <summary>Kern ohne Vorabkennung, z. B. <c>1.1.0-preview.2</c> → <c>1.1.0</c> (Apps aus App Store/TestFlight tragen nur den Kern).</summary>
    public static string Core(string value)
    {
        var (core, _) = Split(value);
        return string.Join('.', core);
    }

    public static string Max(string a, string b) => Compare(a, b) >= 0 ? a : b;

    public static string Min(string a, string b) => Compare(a, b) <= 0 ? a : b;

    private static (int[] Core, string[] Pre) Split(string value)
    {
        var text = Normalize(value);
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        var core = (dash < 0 ? text : text[..dash]).Split('.');
        var numbers = new int[3];
        for (var i = 0; i < 3 && i < core.Length; i++)
        {
            _ = int.TryParse(core[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]);
        }

        return (numbers, dash < 0 ? [] : text[(dash + 1)..].Split('.'));
    }
}
