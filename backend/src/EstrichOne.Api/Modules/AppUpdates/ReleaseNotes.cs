using System.Text.RegularExpressions;
using EstrichOne.Api.Infrastructure.Hosting;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.AppUpdates;

public sealed record AppReleaseSection(string Title, IReadOnlyList<string> Items);

/// <summary>Versionshinweise einer Version, aufbereitet für „Neu in dieser Version“ in den Apps.</summary>
public sealed record AppReleaseNote(string Version, string Title, IReadOnlyList<AppReleaseSection> Sections);

/// <summary>
/// Liest die Versionshinweise (docs/releases/v&lt;version&gt;.md) und bereitet sie für die Apps auf: nur Abschnitte für
/// Benutzer (ohne Server-Update, Installation, Befehle). Gibt es Abschnitte für die Apps („App“, „Apps“, „iPhone“,
/// „Android“ …), zählen nur diese.
/// </summary>
public static partial class ReleaseNotes
{
    private const int MaxSections = 6;
    private const int MaxItems = 10;

    private static readonly string[] TechnicalTitles =
        ["update", "server", "install", "archivserver", "backup-ziel", "upgrade", "entwickl", "technik", "betrieb", "hinweis für administratoren"];

    public static AppReleaseNote Parse(string version, string markdown, string? platform = null)
    {
        var title = $"EstrichONE {version}";
        var sections = new List<(string Title, List<string> Items)>();
        (string Title, List<string> Items)? current = null;
        var inCode = false;
        foreach (var raw in markdown.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }

            if (inCode || line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                title = Clean(line[2..]);
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            {
                current = (Clean(line.TrimStart('#')), new List<string>());
                sections.Add(current.Value);
                continue;
            }

            if (current is null || line.Trim().Length == 0)
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                current.Value.Items.Add(Clean(trimmed[2..]));
            }
            else if (current.Value.Items.Count > 0)
            {
                // Fortsetzungszeile eines Punkts.
                current.Value.Items[^1] = current.Value.Items[^1] + " " + Clean(trimmed);
            }
            else
            {
                current.Value.Items.Add(Clean(trimmed));
            }
        }

        var useful = sections.Where(s => s.Items.Count > 0 && !IsTechnical(s.Title)).ToList();
        var forApps = useful.Where(s => IsForApps(s.Title, platform)).ToList();
        var chosen = forApps.Count > 0 ? forApps : useful.Where(s => !IsForOtherPlatform(s.Title, platform)).ToList();
        return new AppReleaseNote(version, title,
            chosen.Take(MaxSections).Select(s => new AppReleaseSection(s.Title, s.Items.Where(i => i.Length > 0).Take(MaxItems).ToList())).ToList());
    }

    private static bool IsTechnical(string title)
    {
        var lower = title.ToLowerInvariant();
        return TechnicalTitles.Any(lower.Contains);
    }

    private static bool IsForApps(string title, string? platform)
    {
        var words = Words(title);
        var general = words.Contains("app") || words.Contains("apps") || words.Contains("mobile");
        var ios = words.Contains("ios") || words.Contains("iphone") || words.Contains("ipad");
        var android = words.Contains("android");
        return platform switch
        {
            "ios" => general || ios,
            "android" => general || android,
            _ => general || ios || android,
        };
    }

    private static bool IsForOtherPlatform(string title, string? platform)
    {
        var words = Words(title);
        var ios = words.Contains("ios") || words.Contains("iphone") || words.Contains("ipad");
        var android = words.Contains("android");
        return (platform == "ios" && android && !ios) || (platform == "android" && ios && !android);
    }

    private static HashSet<string> Words(string title) =>
        WordPattern().Matches(title.ToLowerInvariant()).Select(m => m.Value).ToHashSet();

    /// <summary>Markdown-Auszeichnung entfernen: Links, Fett, Code.</summary>
    internal static string Clean(string text)
    {
        var result = LinkPattern().Replace(text, "$1");
        result = result.Replace("**", string.Empty, StringComparison.Ordinal).Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
        return result.Trim();
    }

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"[a-zäöüß0-9]+")]
    private static partial Regex WordPattern();
}

/// <summary>Versionshinweise aus dem Verzeichnis <see cref="AppUpdateOptions.ReleaseNotesPath"/> (im Image <c>/app/releases</c>).</summary>
public sealed class ReleaseNotesCatalog(IOptions<AppUpdateOptions> options)
{
    private readonly Lazy<IReadOnlyDictionary<string, string>> _files = new(() => Scan(options.Value.ReleaseNotesPath));

    /// <summary>
    /// Hinweise der Versionen nach <paramref name="from"/> bis einschließlich <paramref name="to"/>, neueste zuerst
    /// (ohne <paramref name="from"/> nur <paramref name="to"/>). Vorabversionen nur, wenn das Ziel selbst eine ist.
    /// </summary>
    public IReadOnlyList<AppReleaseNote> Between(string? from, string to, string? platform, int max = 5)
    {
        var prerelease = to.Contains('-', StringComparison.Ordinal);
        return _files.Value
            .Where(f => VersionComparer.Compare(f.Key, to) <= 0
                && (from is null ? VersionComparer.Compare(f.Key, to) == 0 : VersionComparer.Compare(f.Key, from) > 0)
                && (prerelease || !f.Key.Contains('-', StringComparison.Ordinal)))
            .OrderByDescending(f => f.Key, Comparer<string>.Create(VersionComparer.Compare))
            .Take(Math.Clamp(max, 1, 20))
            .Select(f => ReleaseNotes.Parse(f.Key, ReadSafe(f.Value), platform))
            .Where(n => n.Sections.Count > 0)
            .ToList();
    }

    private static string ReadSafe(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static Dictionary<string, string> Scan(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "v*.md")
            .Select(path => (Version: Path.GetFileNameWithoutExtension(path)[1..], Path: path))
            .Where(f => VersionComparer.IsValid(f.Version))
            .GroupBy(f => VersionComparer.Normalize(f.Version))
            .ToDictionary(g => g.Key, g => g.First().Path);
    }
}
