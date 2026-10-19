using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Infrastructure.Text;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Contacts;

public sealed record ImportIssue(int Line, string Message);

public sealed record ImportDuplicate(int Line, string Name, Guid ExistingContactId);

public sealed record ContactImportResult(
    int TotalRows, int Imported, int WouldImport, IReadOnlyList<ImportIssue> Errors, IReadOnlyList<ImportDuplicate> Duplicates, bool DryRun);

/// <summary>
/// CSV-Import für Kunden. Erste Zeile = Spaltenüberschriften (deutsche Bezeichnungen, Reihenfolge beliebig).
/// Pflicht ist nur „Name“. Fehlerhafte Zeilen und Dubletten werden gemeldet und nie importiert; alle übrigen
/// Zeilen werden in einer Transaktion übernommen. Mit <c>dryRun</c> wird nur geprüft.
/// </summary>
internal sealed class ContactImport(EstrichOneDbContext db)
{
    public const int MaxRows = 5_000;

    private static readonly Dictionary<string, string> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = "name", ["firma"] = "name", ["kunde"] = "name", ["kundenname"] = "name",
        ["kundennummer"] = "number", ["kundennr"] = "number", ["kundennr."] = "number", ["nummer"] = "number",
        ["straße"] = "street", ["strasse"] = "street", ["anschrift"] = "street", ["adresse"] = "street",
        ["plz"] = "postal", ["postleitzahl"] = "postal",
        ["ort"] = "city", ["stadt"] = "city",
        ["telefon"] = "phone", ["tel"] = "phone", ["tel."] = "phone", ["mobil"] = "phone",
        ["e-mail"] = "email", ["email"] = "email", ["mail"] = "email",
        ["art"] = "kind", ["typ"] = "kind",
        ["notiz"] = "notes", ["bemerkung"] = "notes", ["hinweis"] = "notes",
    };

    public async Task<ContactImportResult> ImportAsync(byte[] content, bool dryRun, CancellationToken cancellationToken)
    {
        var rows = CsvDocument.Parse(CsvDocument.Decode(content));
        if (rows.Count == 0)
        {
            return new ContactImportResult(0, 0, 0, [new ImportIssue(1, "Die Datei enthält keine Daten.")], [], dryRun);
        }

        var columns = rows[0].Fields.Select(header => HeaderAliases.GetValueOrDefault(header.Trim())).ToList();
        if (!columns.Contains("name"))
        {
            return new ContactImportResult(rows.Count - 1, 0, 0,
                [new ImportIssue(rows[0].Line, "Die Spalte „Name“ fehlt in der Kopfzeile.")], [], dryRun);
        }

        var dataRows = rows.Skip(1).ToList();
        if (dataRows.Count > MaxRows)
        {
            return new ContactImportResult(dataRows.Count, 0, 0,
                [new ImportIssue(1, $"Höchstens {MaxRows} Zeilen je Import.")], [], dryRun);
        }

        var existing = await db.Set<Contact>().AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.CustomerNumber, c.PostalCode, c.Email })
            .ToListAsync(cancellationToken);
        var byNumber = existing.Where(c => c.CustomerNumber != null).ToDictionary(c => c.CustomerNumber!.ToUpperInvariant(), c => c.Id);
        var byNameAndPostal = existing.GroupBy(c => Key(c.Name, c.PostalCode)).ToDictionary(g => g.Key, g => g.First().Id);
        var byEmail = existing.Where(c => c.Email != null).GroupBy(c => c.Email!.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Id);

        var errors = new List<ImportIssue>();
        var duplicates = new List<ImportDuplicate>();
        var accepted = new List<Contact>();

        foreach (var (line, fields) in dataRows)
        {
            string? Field(string column)
            {
                var index = columns.IndexOf(column);
                var value = index >= 0 && index < fields.Count ? fields[index] : null;
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            var name = Field("name");
            if (name is null)
            {
                errors.Add(new ImportIssue(line, "Name fehlt."));
                continue;
            }

            var problem = Validate(name, Field("number"), Field("postal"), Field("email"), Field("notes"));
            if (problem is not null)
            {
                errors.Add(new ImportIssue(line, problem));
                continue;
            }

            var number = Field("number");
            var postal = Field("postal");
            var email = Field("email");
            Guid? duplicateOf = number is not null && byNumber.TryGetValue(number.ToUpperInvariant(), out var byNo) ? byNo
                : byNameAndPostal.TryGetValue(Key(name, postal), out var byName) ? byName
                : email is not null && byEmail.TryGetValue(email.ToUpperInvariant(), out var byMail) ? byMail
                : null;
            if (duplicateOf is { } existingId)
            {
                duplicates.Add(new ImportDuplicate(line, name, existingId));
                continue;
            }

            var kind = Field("kind")?.StartsWith("p", StringComparison.OrdinalIgnoreCase) == true ? ContactKind.Person : ContactKind.Company;
            var contact = Contact.Create(Guid.CreateVersion7(), kind, name);
            contact.Update(kind, name, number, Field("street"), postal, Field("city"), Field("phone"), email, Field("notes"));
            accepted.Add(contact);

            // Dubletten auch innerhalb derselben Datei erkennen.
            if (number is not null)
            {
                byNumber[number.ToUpperInvariant()] = contact.Id;
            }

            byNameAndPostal[Key(name, postal)] = contact.Id;
            if (email is not null)
            {
                byEmail[email.ToUpperInvariant()] = contact.Id;
            }
        }

        if (!dryRun && accepted.Count > 0)
        {
            db.AddRange(accepted);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new ContactImportResult(dataRows.Count, dryRun ? 0 : accepted.Count, accepted.Count, errors, duplicates, dryRun);
    }

    private static string? Validate(string name, string? number, string? postal, string? email, string? notes) =>
        name.Length > 200 ? "Name ist länger als 200 Zeichen."
        : number is { Length: > 50 } ? "Kundennummer ist länger als 50 Zeichen."
        : postal is not null && (postal.Length > 10 || !postal.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is ' ' or '-'))
            ? $"Ungültige PLZ „{postal}“."
        : email is not null && (email.Length > 320 || !email.Contains('@', StringComparison.Ordinal)) ? $"Ungültige E-Mail-Adresse „{email}“."
        : notes is { Length: > 4000 } ? "Notiz ist länger als 4000 Zeichen."
        : null;

    private static string Key(string name, string? postal) =>
        $"{string.Join(' ', name.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))}|{postal?.Trim()}";
}
