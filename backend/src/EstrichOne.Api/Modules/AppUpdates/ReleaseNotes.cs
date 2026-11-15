using System.Text.RegularExpressions;
using EstrichOne.Api.Infrastructure.Hosting;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.AppUpdates;

public sealed record AppReleaseSection(string Title, IReadOnlyList<string> Items);

/// <summary>Für wen „Was gibt's Neues“ aufbereitet wird: Web (<c>## Neu</c>) oder Apps (<c>## Apps</c>).</summary>
public enum ReleaseNoteAudience
{
    Web,
    Apps,
}

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
    private const int MaxWhatsNewItems = 30;

    private static readonly string[] TechnicalTitles =
        ["update", "aktualisier", "server", "install", "archivserver", "backup-ziel", "upgrade", "entwickl", "technik", "betrieb", "hinweis für administratoren"];

    public static AppReleaseNote Parse(string version, string markdown, string? platform = null)
    {
        var (title, sections) = Read(version, markdown, Clean);
        var useful = sections.Where(s => s.Items.Count > 0 && !IsTechnical(s.Title)).ToList();
        var forApps = useful.Where(s => IsForApps(s.Title, platform)).ToList();
        var chosen = forApps.Count > 0 ? forApps : useful.Where(s => !IsForOtherPlatform(s.Title, platform)).ToList();
        return new AppReleaseNote(version, title,
            chosen.Take(MaxSections).Select(s => new AppReleaseSection(s.Title, s.Items.Where(i => i.Length > 0).Take(MaxItems).ToList())).ToList());
    }

    /// <summary>
    /// „Was gibt's Neues“: für das Web nur „## Neu“ (ältere Hinweise ohne diesen Abschnitt: alle Abschnitte für Benutzer
    /// außer denen für die Apps), für die Apps nur „## Apps“ bzw. die eigene Plattform – sonst nichts. Punkte behalten
    /// <c>**fett**</c> (die Oberflächen setzen es sicher um, kein HTML); Links werden zu Text, Code-Zeichen entfallen.
    /// </summary>
    public static AppReleaseNote ParseFor(string version, string markdown, ReleaseNoteAudience audience, string? platform = null)
    {
        var (title, sections) = Read(version, markdown, Inline);
        var useful = sections.Where(s => s.Items.Count > 0 && !IsTechnical(s.Title)).ToList();
        List<(string Title, List<string> Items)> chosen;
        if (audience == ReleaseNoteAudience.Apps)
        {
            chosen = useful.Where(s => IsForApps(s.Title, platform)).ToList();
        }
        else
        {
            chosen = useful.Where(s => IsNew(s.Title)).ToList();
            if (chosen.Count == 0)
            {
                chosen = useful.Where(s => !IsForApps(s.Title, null)).ToList();
            }
        }

        return new AppReleaseNote(version, title,
            chosen.Take(MaxSections).Select(s => new AppReleaseSection(s.Title, s.Items.Where(i => i.Length > 0).Take(MaxWhatsNewItems).ToList())).ToList());
    }

    private static (string Title, List<(string Title, List<string> Items)> Sections) Read(string version, string markdown, Func<string, string> clean)
    {
        var title = $"EstrichONE {version}";
        var sections = new List<(string Title, List<string> Items)>();
        (string Title, List<string> Items)? current = null;
        var inCode = false;
        var inComment = false;
        foreach (var raw in markdown.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd();
            if (inComment)
            {
                inComment = !line.Contains("-->", StringComparison.Ordinal);
                continue;
            }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                continue;
            }

            if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal))
            {
                // Mehrzeilige Kommentare (Vorlage) ganz überspringen.
                inComment = !line.Contains("-->", StringComparison.Ordinal);
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
                current.Value.Items.Add(clean(trimmed[2..]));
            }
            else if (current.Value.Items.Count > 0)
            {
                // Fortsetzungszeile eines Punkts.
                current.Value.Items[^1] = current.Value.Items[^1] + " " + clean(trimmed);
            }
            else
            {
                current.Value.Items.Add(clean(trimmed));
            }
        }

        // Platzhalter der Vorlage („- …“) zählen nicht.
        foreach (var section in sections)
        {
            section.Items.RemoveAll(i => i.Trim('…', '.', ' ').Length == 0);
        }

        return (title, sections);
    }

    private static bool IsNew(string title)
    {
        var words = Words(title);
        return words.Contains("neu") || words.Contains("neues") || words.Contains("neuerungen");
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

    /// <summary>Für „Was gibt's Neues“: wie <see cref="Clean"/>, aber <c>**fett**</c> bleibt erhalten.</summary>
    internal static string Inline(string text)
    {
        var result = LinkPattern().Replace(text, "$1");
        result = result.Replace("__", "**", StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal);
        // Ungerade Anzahl „**“: das letzte entfernen, damit nichts bis zum Ende fett wird.
        var count = (result.Length - result.Replace("**", string.Empty, StringComparison.Ordinal).Length) / 2;
        if (count % 2 == 1)
        {
            var last = result.LastIndexOf("**", StringComparison.Ordinal);
            result = result.Remove(last, 2);
        }

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

    /// <summary>
    /// „Was gibt's Neues“: Versionen nach <paramref name="since"/> (ohne: die neuesten) bis einschließlich
    /// <paramref name="to"/>, neueste zuerst, nur mit Inhalt für <paramref name="audience"/>, höchstens <paramref name="max"/>.
    /// Vorabversionen nur, wenn das Ziel selbst eine ist.
    /// </summary>
    public IReadOnlyList<AppReleaseNote> Recent(string? since, string to, ReleaseNoteAudience audience, string? platform, int max)
    {
        var prerelease = to.Contains('-', StringComparison.Ordinal);
        return _files.Value
            .Where(f => VersionComparer.Compare(f.Key, to) <= 0
                && (since is null || VersionComparer.Compare(f.Key, since) > 0)
                && (prerelease || !f.Key.Contains('-', StringComparison.Ordinal)))
            .OrderByDescending(f => f.Key, Comparer<string>.Create(VersionComparer.Compare))
            .Select(f => ReleaseNotes.ParseFor(f.Key, ReadSafe(f.Value), audience, platform))
            .Where(n => n.Sections.Count > 0)
            .Take(Math.Clamp(max, 1, 50))
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
