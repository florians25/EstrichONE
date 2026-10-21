using System.Globalization;
using System.Text;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Contacts;

/// <summary>Möglicher Doppelgänger eines neu zu erfassenden Kunden, mit Begründung („gleiche Telefonnummer“ …).</summary>
public sealed record ContactDuplicateCandidate(ContactResponse Contact, int Score, IReadOnlyList<string> Reasons);

/// <summary>
/// Dublettenprüfung vor dem Anlegen eines Kunden (Web-Assistent und iOS): ähnlicher Name, gleiche Telefonnummer,
/// E-Mail oder Anschrift. Die Oberfläche zeigt die Treffer und lässt bewusst einen vorhandenen Kunden wählen oder
/// trotzdem neu anlegen – die Prüfung blockiert nie.
/// </summary>
internal static class ContactDuplicates
{
    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "familie", "fam", "herr", "frau", "hr", "fr", "und", "gmbh", "co", "kg", "ag", "ug", "mbh", "gbr", "ek", "e", "k", "dr", "ing", "dipl",
    };

    public static void Map(RouteGroupBuilder contacts) =>
        contacts.MapGet("/duplicates", FindAsync).WithName("FindContactDuplicates")
            .WithSummary("Mögliche Dubletten vor dem Anlegen eines Kunden (Name, Telefon, E-Mail, Anschrift); höchstens 10 Treffer, bester zuerst")
            .RequirePermission(Permissions.ContactsRead);

    private static async Task<Ok<List<ContactDuplicateCandidate>>> FindAsync(EstrichOneDbContext db, CancellationToken cancellationToken,
        [FromQuery] string? name = null, [FromQuery] string? street = null, [FromQuery] string? postalCode = null, [FromQuery] string? city = null,
        [FromQuery] string? phone = null, [FromQuery] string? email = null)
    {
        var tokens = Tokens(name);
        var digits = Digits(phone);
        var mail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        var streetKey = Normalize(street);
        var postal = string.IsNullOrWhiteSpace(postalCode) ? null : postalCode.Trim();
        if (tokens.Count == 0 && digits is null && mail is null && (streetKey.Length == 0 || postal is null))
        {
            return TypedResults.Ok(new List<ContactDuplicateCandidate>());
        }

        // Vorauswahl in der Datenbank (Namensbestandteile, E-Mail, PLZ); die Bewertung erfolgt anschließend im Speicher.
        var query = db.Set<Contact>().AsNoTracking();
        var patterns = tokens.Where(t => t.Length >= 3).Select(t => "%" + t[..Math.Min(t.Length, 4)] + "%").Distinct().ToList();
        var phoneTail = digits is { Length: >= 6 } ? "%" + digits[^6..] : null;
        // Weitere E-Mail-Adressen stehen in der jsonb-Liste.
        var mailIds = mail is null ? [] : await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM contacts
            WHERE deleted_at IS NULL AND EXISTS (SELECT 1 FROM jsonb_array_elements(emails) e WHERE lower(e->>'address') = lower({mail}))
            """).ToListAsync(cancellationToken);
        var candidates = await query.Where(c =>
                patterns.Any(p => EF.Functions.ILike(c.Name, p))
                || (mail != null && c.Email != null && EF.Functions.ILike(c.Email, mail))
                || mailIds.Contains(c.Id)
                || (postal != null && c.PostalCode == postal)
                || (phoneTail != null && c.Phone != null))
            .OrderBy(c => c.Name).Take(2000).ToListAsync(cancellationToken);

        var result = new List<ContactDuplicateCandidate>();
        foreach (var contact in candidates)
        {
            var reasons = new List<string>();
            var score = 0;
            var other = Tokens(contact.Name);
            if (tokens.Count > 0 && other.Count > 0)
            {
                var exact = tokens.Count(t => other.Contains(t));
                var common = tokens.Count(t => other.Any(o => o == t || (t.Length >= 4 && o.Length >= 4 && Distance(t, o) <= 1)));
                var size = Math.Max(tokens.Count, other.Count);
                if (exact == size)
                {
                    score += 60;
                    reasons.Add("gleicher Name");
                }
                else if (common > 0)
                {
                    score += (int)Math.Round(40.0 * common / size) + 10;
                    reasons.Add("ähnlicher Name");
                }
            }

            // Alle Nummern des Kunden (Mobil, Festnetz, Büro …), nicht nur die erste.
            if (digits is { Length: >= 6 } && contact.Phones.Select(x => x.Number).Append(contact.Phone).Select(Digits).Any(theirs => theirs is { Length: > 0 }
                && (theirs.EndsWith(digits[^Math.Min(digits.Length, 8)..], StringComparison.Ordinal)
                    || digits.EndsWith(theirs[^Math.Min(theirs.Length, 8)..], StringComparison.Ordinal))))
            {
                score += 45;
                reasons.Add("gleiche Telefonnummer");
            }

            if (mail is not null && contact.Emails.Select(x => x.Address).Append(contact.Email).Any(x => string.Equals(x?.Trim(), mail, StringComparison.OrdinalIgnoreCase)))
            {
                score += 50;
                reasons.Add("gleiche E-Mail-Adresse");
            }

            if (postal is not null && contact.PostalCode == postal && streetKey.Length > 0 && Normalize(contact.Street) == streetKey)
            {
                score += 40;
                reasons.Add("gleiche Anschrift");
            }
            else if (reasons.Count > 0 && !string.IsNullOrWhiteSpace(city) && string.Equals(contact.City?.Trim(), city.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
                reasons.Add("gleicher Ort");
            }

            if (score >= 35)
            {
                result.Add(new ContactDuplicateCandidate(ContactEndpoints.ToResponse(contact), Math.Min(score, 100), reasons));
            }
        }

        return TypedResults.Ok(result.OrderByDescending(r => r.Score).ThenBy(r => r.Contact.Name, StringComparer.CurrentCulture).Take(10).ToList());
    }

    internal static List<string> Tokens(string? value) =>
        Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !NoiseWords.Contains(t)).Distinct().ToList();

    /// <summary>Kleinbuchstaben, Umlaute ausgeschrieben, Satzzeichen entfernt, „str.“ → „strasse“.</summary>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim().ToLowerInvariant()
            .Replace("ä", "ae", StringComparison.Ordinal).Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal).Replace("ß", "ss", StringComparison.Ordinal);
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t is "str" ? "strasse" : t.EndsWith("str", StringComparison.Ordinal) && t.Length > 4 ? t[..^3] + "strasse" : t));
    }

    internal static string? Digits(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0049", StringComparison.Ordinal))
        {
            digits = "0" + digits[4..];
        }
        else if (phone.TrimStart().StartsWith('+') && digits.StartsWith("49", StringComparison.Ordinal))
        {
            digits = "0" + digits[2..];
        }

        return digits.Length >= 6 ? digits : null;
    }

    /// <summary>Editierabstand mit Vertauschung benachbarter Buchstaben („Schnieder“ ↔ „Schneider“ = 1).</summary>
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
