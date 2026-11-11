using System.Globalization;
using System.Text;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>Artikel in Listen und Vorschlägen (ohne Inhalt).</summary>
public sealed record KnowledgeArticleSummary(
    Guid Id, string Title, TicketCategory? Category, string? Keywords, bool IsPublished, string Excerpt, int Views, int HelpfulCount,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Einfache Relevanzsuche für die Wissensdatenbank (rein, ohne Datenbank – siehe Tests): Wörter ab drei Zeichen, ohne
/// Füllwörter und Groß-/Kleinschreibung, Umlaute vereinheitlicht. Treffer im Titel zählen dreifach, in Suchbegriffen
/// doppelt, im Text einfach; die passende Kategorie gibt einen Bonus.
/// </summary>
public static class KnowledgeSearch
{
    private static readonly HashSet<string> StopWords =
    [
        "und", "oder", "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einer", "ist", "sind", "nicht", "kein", "keine", "mit",
        "auf", "bei", "von", "für", "fuer", "seit", "nach", "wie", "was", "wird", "werden", "geht", "mehr", "mein", "meine", "ich", "wir", "sie",
        "auch", "noch", "wurde", "worden", "haben", "einem", "eines", "diese", "dieser", "nur", "dass", "wenn", "aber", "kann", "habe", "hat",
        "heute", "immer", "beim", "zum", "zur", "the", "and",
    ];

    public static IReadOnlyList<string> Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = Fold(text);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w)).Distinct().ToList();
    }

    private static string Fold(string text) => text.ToLowerInvariant()
        .Replace("ä", "ae", StringComparison.Ordinal).Replace("ö", "oe", StringComparison.Ordinal).Replace("ü", "ue", StringComparison.Ordinal)
        .Replace("ß", "ss", StringComparison.Ordinal);

    public static double Score(KnowledgeArticle article, IReadOnlyList<string> words, TicketCategory? category)
    {
        if (words.Count == 0)
        {
            return 0;
        }

        var title = Fold(article.Title);
        var keywords = Fold(article.Keywords ?? string.Empty);
        var body = Fold(article.Body);
        double score = 0;
        foreach (var word in words)
        {
            // Wortanfang reicht („drucker“ findet „druckertreiber“, „anmeld“ findet „anmeldung“).
            var stem = word.Length > 6 ? word[..6] : word;
            score += (title.Contains(stem, StringComparison.Ordinal) ? 3 : 0) + (keywords.Contains(stem, StringComparison.Ordinal) ? 2 : 0)
                + (body.Contains(stem, StringComparison.Ordinal) ? 1 : 0);
        }

        return score == 0 ? 0 : score + (category is { } c && article.Category == c ? 1.5 : 0);
    }

    public static IReadOnlyList<KnowledgeArticleSummary> Rank(IEnumerable<KnowledgeArticle> articles, string? query, TicketCategory? category,
        bool publishedOnly, int take = 10)
    {
        var words = Words(query);
        return articles.Where(a => !publishedOnly || a.IsPublished)
            .Select(a => (Article: a, Score: Score(a, words, category)))
            .Where(x => x.Score >= 2)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Article.HelpfulCount).ThenBy(x => x.Article.Title, StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true))
            .Take(take).Select(x => Summarize(x.Article)).ToList();
    }

    public static KnowledgeArticleSummary Summarize(KnowledgeArticle a)
    {
        var body = a.Body.ReplaceLineEndings(" ").Trim();
        return new KnowledgeArticleSummary(a.Id, a.Title, a.Category, a.Keywords, a.IsPublished, body.Length > 180 ? body[..180].TrimEnd() + " …" : body,
            a.Views, a.HelpfulCount, a.UpdatedAt);
    }
}
