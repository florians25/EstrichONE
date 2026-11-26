using System.Globalization;
using System.Text;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Ein Kunde bzw. Kontakt in der Form, in der die Dublettenprüfung ihn vergleicht (gleich für EstrichONE und Lexware Office).</summary>
/// <param name="Key">ID im jeweiligen System.</param>
public sealed record MatchParty(
    string Key, string Name, bool IsCompany, IReadOnlyList<string> Emails, IReadOnlyList<string> Phones, string? Street, string? PostalCode,
    string? City, string? CustomerNumber, string? VatId);

/// <param name="Score">0 bis 100; ab <see cref="LexwareMatching.Threshold"/> gilt der Eintrag als mögliche Dublette.</param>
/// <param name="Reasons">Übereinstimmungsgründe als Text, z. B. „gleiche E-Mail-Adresse“.</param>
/// <param name="Strong">Eindeutiges Merkmal stimmt überein (E-Mail-Adresse, Kundennummer mit passendem Namen oder USt-ID).</param>
public sealed record MatchResult(int Score, IReadOnlyList<string> Reasons, bool Strong);

/// <summary>
/// Dublettenprüfung zwischen EstrichONE-Kunden und Lexware-Kontakten (in beide Richtungen und innerhalb eines Systems).
/// Reine Funktionen ohne Datenbank und Netz: Namen werden normalisiert (Groß- und Kleinschreibung, Umlaute, Satzzeichen,
/// Rechtsformzusätze, Anreden und Titel, Reihenfolge der Namensteile), Telefonnummern auf Ziffern reduziert (+49 und 0049
/// wie 0), Straßen vereinheitlicht („Str.“ wie „Straße“). Die Prüfung schlägt nur vor; verknüpft oder angelegt wird erst
/// nach Bestätigung.
/// </summary>
public static class LexwareMatching
{
    public const int Threshold = 40;

    public const string SameEmail = "gleiche E-Mail-Adresse";
    public const string SamePhone = "gleiche Telefonnummer";
    public const string SameNameAndPostalCode = "gleicher Name und PLZ";
    public const string SameName = "gleicher Name";
    public const string SimilarName = "ähnlicher Name";
    public const string SameAddress = "gleiche Anschrift";
    public const string SameCustomerNumber = "gleiche Kundennummer";
    public const string SameVatId = "gleiche USt-ID";
    public const string DifferentKind = "Privatperson und Firma";

    /// <summary>Rechtsformen, Anreden, Titel und Füllwörter, die beim Namensvergleich nicht zählen.</summary>
    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "gmbh", "mbh", "co", "kg", "kgaa", "ag", "ug", "haftungsbeschraenkt", "gbr", "ohg", "ek", "e", "k", "ev", "v", "se", "partg", "partgmbb",
        "inh", "inhaber", "firma", "fa", "herr", "herrn", "frau", "familie", "fam", "eheleute", "hr", "fr", "dr", "prof", "dipl", "ing", "und", "u",
    };

    /// <summary>Vergleicht zwei Einträge; <c>null</c>, wenn nichts für eine Dublette spricht.</summary>
    public static MatchResult? Compare(MatchParty a, MatchParty b) => Compare(Prepare(a), Prepare(b));

    /// <summary>Mögliche Dubletten von <paramref name="subject"/> in <paramref name="pool"/>, bester Treffer zuerst.</summary>
    public static IReadOnlyList<(MatchParty Party, MatchResult Match)> Find(MatchParty subject, IEnumerable<MatchParty> pool, int limit = 10)
    {
        var prepared = Prepare(subject);
        return pool.Where(p => p.Key != subject.Key).Select(p => (Party: p, Match: Compare(prepared, Prepare(p))))
            .Where(r => r.Match is not null).Select(r => (r.Party, Match: r.Match!))
            .OrderByDescending(r => r.Match.Score).ThenBy(r => r.Party.Name, StringComparer.OrdinalIgnoreCase).Take(limit).ToList();
    }

    /// <summary>
    /// Alle Paare möglicher Dubletten zwischen zwei Listen. Für die Abgleichübersicht: vergleicht nur Einträge, die
    /// mindestens ein Merkmal teilen (Namensteil, E-Mail, Telefon, PLZ mit Straße, Nummer), statt jeden mit jedem.
    /// </summary>
    public static IReadOnlyList<(MatchParty Left, MatchParty Right, MatchResult Match)> Pairs(IReadOnlyList<MatchParty> left, IReadOnlyList<MatchParty> right)
    {
        var index = new Dictionary<string, List<Prepared>>(StringComparer.Ordinal);
        foreach (var party in right.Select(Prepare))
        {
            foreach (var key in party.BlockingKeys())
            {
                if (!index.TryGetValue(key, out var bucket))
                {
                    index[key] = bucket = [];
                }

                bucket.Add(party);
            }
        }

        var result = new List<(MatchParty, MatchParty, MatchResult)>();
        foreach (var party in left.Select(Prepare))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in party.BlockingKeys())
            {
                if (!index.TryGetValue(key, out var bucket))
                {
                    continue;
                }

                foreach (var other in bucket.Where(o => seen.Add(o.Party.Key)))
                {
                    if (Compare(party, other) is { } match)
                    {
                        result.Add((party.Party, other.Party, match));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Doppelte Einträge innerhalb einer Liste (jedes Paar einmal).</summary>
    public static IReadOnlyList<(MatchParty Left, MatchParty Right, MatchResult Match)> PairsWithin(IReadOnlyList<MatchParty> parties) =>
        Pairs(parties, parties).Where(p => string.CompareOrdinal(p.Left.Key, p.Right.Key) < 0).ToList();

    private static MatchResult? Compare(Prepared a, Prepared b)
    {
        var reasons = new List<string>();
        var score = 0;
        var strong = false;

        var sameName = a.NameKey.Length > 0 && a.NameKey == b.NameKey;
        var similarity = sameName ? 0 : Similarity(a.Tokens, b.Tokens);
        var similarName = similarity > 0;
        var samePostal = a.PostalCode is not null && a.PostalCode == b.PostalCode;

        if (a.Emails.Overlaps(b.Emails))
        {
            score += 85;
            strong = true;
            reasons.Add(SameEmail);
        }

        if (a.VatId is not null && a.VatId == b.VatId)
        {
            score += 90;
            strong = true;
            reasons.Add(SameVatId);
        }

        if (a.Number is not null && a.Number == b.Number)
        {
            // Die Nummernkreise beider Systeme sind unabhängig; eine zufällig gleiche Nummer allein ist kein Beleg.
            if (sameName || similarName)
            {
                score += 80;
                strong = true;
            }
            else
            {
                score += 40;
            }

            reasons.Add(SameCustomerNumber);
        }

        if (a.Phones.Overlaps(b.Phones))
        {
            score += 45;
            reasons.Add(SamePhone);
        }

        if (sameName && samePostal)
        {
            score += 80;
            reasons.Add(SameNameAndPostalCode);
        }
        else if (sameName)
        {
            score += 55;
            reasons.Add(SameName);
        }
        else if (similarName)
        {
            // Enthält ein Name den anderen vollständig (oder nur mit Tippfehler), zählt das auch ohne gleiche PLZ als Hinweis.
            score += (similarity == 2 ? 40 : 30) + (samePostal ? 15 : 0);
            reasons.Add(SimilarName);
        }

        if (samePostal && a.StreetKey.Length > 0 && a.StreetKey == b.StreetKey)
        {
            // Gleiche Anschrift allein (Nachbarn, Mehrfamilienhaus, Bauträger und Bauherr) reicht nicht.
            score += 30;
            reasons.Add(SameAddress);
        }

        if (score < Threshold)
        {
            return null;
        }

        if (a.Party.IsCompany != b.Party.IsCompany && !strong)
        {
            // „Hans Müller“ und „Hans Müller GmbH“ können verschiedene Rechnungsempfänger sein.
            score -= 15;
            reasons.Add(DifferentKind);
            if (score < Threshold)
            {
                return null;
            }
        }

        return new MatchResult(Math.Min(score, 100), reasons, strong);
    }

    /// <summary>
    /// 0 = verschieden; 1 = mindestens die Hälfte der Namensteile stimmt überein; 2 = alle Teile des kürzeren Namens (mindestens
    /// zwei) kommen im anderen vor. Tippfehler mit einem Zeichen Abstand zählen als Übereinstimmung.
    /// </summary>
    private static int Similarity(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var common = a.Count(t => b.Any(o => o == t || (t.Length >= 5 && o.Length >= 5 && Distance(t, o) <= 1)));
        var size = Math.Max(a.Count, b.Count);
        if (common >= 2 && common == Math.Min(a.Count, b.Count))
        {
            return 2;
        }

        return common > 0 && common * 2 >= size && (common >= 2 || size <= 2) ? 1 : 0;
    }

    private sealed record Prepared(
        MatchParty Party, string NameKey, IReadOnlyList<string> Tokens, HashSet<string> Emails, HashSet<string> Phones, string StreetKey,
        string? PostalCode, string? Number, string? VatId)
    {
        public IEnumerable<string> BlockingKeys()
        {
            foreach (var token in Tokens.Where(t => t.Length >= 3))
            {
                yield return "n:" + token[..Math.Min(token.Length, 4)];
            }

            foreach (var email in Emails)
            {
                yield return "e:" + email;
            }

            foreach (var phone in Phones)
            {
                yield return "p:" + phone;
            }

            if (Number is not null)
            {
                yield return "c:" + Number;
            }

            if (VatId is not null)
            {
                yield return "v:" + VatId;
            }
        }
    }

    private static Prepared Prepare(MatchParty party)
    {
        var tokens = NameTokens(party.Name);
        return new Prepared(party, string.Join(' ', tokens.Order(StringComparer.Ordinal)), tokens,
            party.Emails.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal),
            party.Phones.Select(PhoneKey).Where(p => p is not null).Select(p => p!).ToHashSet(StringComparer.Ordinal),
            Normalize(party.Street), Blank(party.PostalCode), Blank(party.CustomerNumber)?.ToLowerInvariant(), VatKey(party.VatId));
    }

    /// <summary>Namensteile ohne Rechtsform, Anrede und Titel („Müller Estrich GmbH &amp; Co. KG“ wird zu „mueller“, „estrich“).</summary>
    public static IReadOnlyList<string> NameTokens(string? name) =>
        Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !NoiseWords.Contains(t)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Kleinbuchstaben, Umlaute ausgeschrieben, Akzente und Satzzeichen entfernt, „Str.“ zu „strasse“.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim().ToLowerInvariant()
            .Replace("ä", "ae", StringComparison.Ordinal).Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal).Replace("ß", "ss", StringComparison.Ordinal)
            .Replace("e.k.", "ek", StringComparison.Ordinal).Replace("e. k.", "ek", StringComparison.Ordinal)
            .Replace("e.v.", "ev", StringComparison.Ordinal).Replace("e. v.", "ev", StringComparison.Ordinal);
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t is "str" ? "strasse" : t.EndsWith("str", StringComparison.Ordinal) && t.Length > 4 ? t[..^3] + "strasse" : t));
    }

    /// <summary>Die letzten acht Ziffern der Nummer nach Vereinheitlichung der Landesvorwahl; <c>null</c> bei weniger als sechs Ziffern.</summary>
    public static string? PhoneKey(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("0049", StringComparison.Ordinal))
        {
            digits = "0" + digits[4..];
        }
        else if (phone.TrimStart().StartsWith('+') && digits.StartsWith("49", StringComparison.Ordinal))
        {
            digits = "0" + digits[2..];
        }

        return digits.Length < 6 ? null : digits[^Math.Min(digits.Length, 8)..];
    }

    private static string? VatKey(string? vatId)
    {
        var key = new string((vatId ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return key.Length >= 8 ? key : null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Editierabstand mit Vertauschung benachbarter Buchstaben („Schnieder“ und „Schneider“ haben Abstand 1).</summary>
    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }
}
