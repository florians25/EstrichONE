using System.Globalization;

namespace EstrichOne.Hosting.Domain;

/// <summary>Vergleich von Versionen der Form 1.2.3 bzw. 1.2.3-preview.4 nach SemVer 2.0 (Vorabversion &lt; Release).</summary>
public sealed record SemanticVersion(int Major, int Minor, int Patch, string[] PreRelease) : IComparable<SemanticVersion>
{
    public static SemanticVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim().TrimStart('v');
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var core = dash >= 0 ? value[..dash] : value;
        var pre = dash >= 0 ? value[(dash + 1)..].Split('.') : [];
        var parts = core.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch)
            || pre.Any(string.IsNullOrEmpty))
        {
            return null;
        }

        return new SemanticVersion(major, minor, patch, pre);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        if (PreRelease.Length == 0 || other.PreRelease.Length == 0)
        {
            // Ohne Vorabkennung ist eine Version neuer als jede Vorabversion derselben Nummer.
            return (PreRelease.Length == 0, other.PreRelease.Length == 0) switch
            {
                (true, true) => 0,
                (true, false) => 1,
                _ => -1,
            };
        }

        for (var i = 0; i < Math.Min(PreRelease.Length, other.PreRelease.Length); i++)
        {
            var a = PreRelease[i];
            var b = other.PreRelease[i];
            var aNumeric = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var aNumber);
            var bNumeric = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bNumber);
            result = (aNumeric, bNumeric) switch
            {
                (true, true) => aNumber.CompareTo(bNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a, b),
            };
            if (result != 0)
            {
                return result;
            }
        }

        return PreRelease.Length.CompareTo(other.PreRelease.Length);
    }

    /// <summary>Ist <paramref name="version"/> älter als <paramref name="target"/>? Unlesbare Versionen gelten als veraltet.</summary>
    public static bool IsOlder(string? version, string? target)
    {
        var current = TryParse(version);
        var wanted = TryParse(target);
        if (wanted is null)
        {
            return false;
        }

        return current is null || current.CompareTo(wanted) < 0;
    }

    public override string ToString() =>
        PreRelease.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{string.Join('.', PreRelease)}");

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
}
