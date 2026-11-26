using System.Text;
using System.Text.Json;
using EstrichOne.Api.Modules.Contacts.Exports;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Richtung, in der spätere Änderungen an verknüpften Kunden abgeglichen werden.</summary>
public enum LexwareChangeSync
{
    /// <summary>Beide Richtungen (Vorgabe).</summary>
    Both,

    /// <summary>Nur Änderungen aus EstrichONE gehen nach Lexware Office.</summary>
    ToLexware,

    /// <summary>Nur Änderungen aus Lexware Office kommen nach EstrichONE.</summary>
    FromLexware,

    /// <summary>Kein Änderungsabgleich.</summary>
    Off,
}

/// <summary>Felder, die zwischen Kunde und Lexware-Kontakt abgeglichen werden. Kundennummern gehören nie dazu.</summary>
public static class LexwareSyncFields
{
    public const string Name = "name";
    public const string Salutation = "salutation";
    public const string Street = "street";
    public const string PostalCode = "postalCode";
    public const string City = "city";
    public const string Email = "email";
    public const string Phone = "phone";
    public const string VatId = "vatId";

    public static IReadOnlyList<string> All { get; } = [Name, Salutation, Street, PostalCode, City, Email, Phone, VatId];

    public static string Label(string field) => field switch
    {
        Name => "Name",
        Salutation => "Anrede",
        Street => "Straße",
        PostalCode => "PLZ",
        City => "Ort",
        Email => "E-Mail-Adresse",
        Phone => "Telefon",
        VatId => "USt-ID",
        _ => field,
    };
}

/// <summary>Abweichung in einem Feld: Beide Seiten haben unterschiedliche Werte und keine wurde automatisch übernommen.</summary>
public sealed record LexwareFieldDeviation(string Field, string? EstrichOne, string? Lexware);

/// <summary>Ergebnis des Dreiwege-Abgleichs eines Kunden mit seinem Lexware-Kontakt.</summary>
/// <param name="Baseline">Zuletzt abgeglichener Stand je Feld nach diesem Abgleich (Felder mit Abweichung behalten ihren bisherigen Stand).</param>
/// <param name="ToLexware">Felder, deren Wert aus EstrichONE nach Lexware Office geht.</param>
/// <param name="ToEstrichOne">Felder, deren Wert aus Lexware Office nach EstrichONE kommt.</param>
/// <param name="Deviations">Felder, über die der Nutzer entscheidet.</param>
public sealed record LexwareMergeResult(
    IReadOnlyDictionary<string, string?> Baseline, IReadOnlyDictionary<string, string?> ToLexware, IReadOnlyDictionary<string, string?> ToEstrichOne,
    IReadOnlyList<LexwareFieldDeviation> Deviations);

/// <summary>
/// Änderungsabgleich verknüpfter Kunden als reine Funktionen: Feldwerte beider Seiten, Vergleich und Dreiwege-Abgleich
/// gegen den zuletzt abgeglichenen Stand. Keine Datenbank, kein Netz (mit Unit-Tests geprüft).
/// </summary>
public static class LexwareContactSync
{
    public const string EstrichOne = "estrichOne";
    public const string Lexware = "lexware";

    /// <summary>
    /// Dreiwege-Abgleich je Feld. Sind beide Seiten gleich, ist nichts zu tun. Hat nur eine Seite das Feld seit dem letzten
    /// Abgleich geändert, wird ihr Wert übernommen, sofern die Richtung das zulässt. Haben beide Seiten das Feld
    /// unterschiedlich geändert, gibt es noch keinen abgeglichenen Stand (Erstabgleich) oder ist die Richtung gesperrt,
    /// bleibt das Feld als Abweichung stehen: Überschrieben wird dann nichts. <paramref name="choices"/> sind die
    /// Entscheidungen des Nutzers je Feld (<see cref="EstrichOne"/> oder <see cref="Lexware"/>) und gelten unabhängig von der Richtung.
    /// </summary>
    /// <param name="remote">Werte in Lexware Office; nur die hier genannten Felder werden abgeglichen.</param>
    public static LexwareMergeResult Merge(IReadOnlyDictionary<string, string?>? baseline, IReadOnlyDictionary<string, string?> local,
        IReadOnlyDictionary<string, string?> remote, LexwareChangeSync direction, IReadOnlyDictionary<string, string>? choices = null)
    {
        var next = new Dictionary<string, string?>(StringComparer.Ordinal);
        var toLexware = new Dictionary<string, string?>(StringComparer.Ordinal);
        var toEstrichOne = new Dictionary<string, string?>(StringComparer.Ordinal);
        var deviations = new List<LexwareFieldDeviation>();
        var push = direction is LexwareChangeSync.Both or LexwareChangeSync.ToLexware;
        var pull = direction is LexwareChangeSync.Both or LexwareChangeSync.FromLexware;
        foreach (var field in LexwareSyncFields.All)
        {
            if (!remote.TryGetValue(field, out var theirs))
            {
                continue;
            }

            var mine = Clean(local.GetValueOrDefault(field));
            theirs = Clean(theirs);
            if (Same(field, mine, theirs))
            {
                next[field] = mine ?? theirs;
                continue;
            }

            var known = baseline is not null && baseline.ContainsKey(field);
            var before = known ? Clean(baseline![field]) : null;
            var localChanged = !known || !Same(field, mine, before);
            var remoteChanged = !known || !Same(field, theirs, before);
            var choice = choices?.GetValueOrDefault(field);

            // Der Name ist in beiden Systemen Pflicht: Ein leerer Wert überschreibt nie einen vorhandenen.
            if (choice == EstrichOne && !(field == LexwareSyncFields.Name && mine is null))
            {
                toLexware[field] = mine;
                next[field] = mine;
            }
            else if (choice == Lexware && !(field == LexwareSyncFields.Name && theirs is null))
            {
                toEstrichOne[field] = theirs;
                next[field] = theirs;
            }
            else if (known && push && localChanged && !remoteChanged && !(field == LexwareSyncFields.Name && mine is null))
            {
                toLexware[field] = mine;
                next[field] = mine;
            }
            else if (known && pull && remoteChanged && !localChanged && !(field == LexwareSyncFields.Name && theirs is null))
            {
                toEstrichOne[field] = theirs;
                next[field] = theirs;
            }
            else
            {
                deviations.Add(new LexwareFieldDeviation(field, mine, theirs));
                if (known)
                {
                    next[field] = before;
                }
            }
        }

        return new LexwareMergeResult(next, toLexware, toEstrichOne, deviations);
    }

    /// <summary>Gleich im Sinne des Abgleichs: ohne Rand- und Mehrfachleerzeichen; E-Mail ohne Groß- und Kleinschreibung; Telefon nur nach Ziffern.</summary>
    public static bool Same(string field, string? left, string? right)
    {
        var a = Clean(left);
        var b = Clean(right);
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return field switch
        {
            LexwareSyncFields.Email => string.Equals(a, b, StringComparison.OrdinalIgnoreCase),
            LexwareSyncFields.Phone => Digits(a) is { Length: > 0 } x && x == Digits(b) || string.Equals(a, b, StringComparison.Ordinal),
            LexwareSyncFields.VatId => string.Equals(a.Replace(" ", string.Empty, StringComparison.Ordinal), b.Replace(" ", string.Empty, StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(a, b, StringComparison.Ordinal),
        };
    }

    /// <summary>Rand- und Mehrfachleerzeichen entfernen; leer ist wie nicht vorhanden.</summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var part in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(builder.Length > 0 ? " " : string.Empty).Append(part);
        }

        return builder.ToString();
    }

    /// <summary>Telefonnummer nur nach Ziffern, <c>+49</c> und <c>0049</c> wie eine führende <c>0</c>.</summary>
    private static string Digits(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (value.TrimStart().StartsWith('+') && digits.StartsWith("49", StringComparison.Ordinal))
        {
            return "0" + digits[2..];
        }

        return digits.StartsWith("0049", StringComparison.Ordinal) ? "0" + digits[4..] : digits;
    }

    /// <summary>
    /// Abgeglichene Felder des Kunden. Rechnungsanschrift ist die abweichende Rechnungsanschrift, sonst die Anschrift des
    /// Kunden; E-Mail-Adresse und Telefon sind jeweils der erste Eintrag (Telefon ohne Fax). Personen werden als
    /// „Vorname Nachname“ verglichen.
    /// </summary>
    public static Dictionary<string, string?> Local(Contact contact, bool lexwareIsCompany)
    {
        var billing = BillingInUse(contact);
        var name = contact.Name;
        if (!lexwareIsCompany)
        {
            var (first, last) = LexwareService.SplitPersonName(contact.Name);
            name = string.Join(' ', new[] { first, last }.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [LexwareSyncFields.Name] = Clean(name),
            [LexwareSyncFields.Salutation] = Clean(contact.Salutation),
            [LexwareSyncFields.Street] = Clean(billing ? contact.BillingAddress!.Street : contact.Street),
            [LexwareSyncFields.PostalCode] = Clean(billing ? contact.BillingAddress!.PostalCode : contact.PostalCode),
            [LexwareSyncFields.City] = Clean(billing ? contact.BillingAddress!.City : contact.City),
            [LexwareSyncFields.Email] = Clean(contact.Emails.FirstOrDefault()?.Address ?? contact.Email),
            [LexwareSyncFields.Phone] = Clean(PrimaryPhone(contact)),
            [LexwareSyncFields.VatId] = Clean(contact.VatId),
        };
    }

    /// <summary>Der Kunde hat eine abweichende Rechnungsanschrift mit Straße, PLZ oder Ort.</summary>
    public static bool BillingInUse(Contact contact) =>
        contact.BillingAddress is { } b && (Clean(b.Street) ?? Clean(b.PostalCode) ?? Clean(b.City)) is not null;

    public static string? PrimaryPhone(Contact contact) =>
        contact.Phones.Count > 0 ? contact.Phones.FirstOrDefault(p => p.Kind != ContactPhoneKind.Fax)?.Number : contact.Phone;

    /// <summary>
    /// Abgeglichene Felder des Lexware-Kontakts. Anrede nur bei Personen, USt-ID nur bei Firmen (so führt Lexware Office
    /// sie). Hat der Kontakt mehrere E-Mail-Adressen oder Telefonnummern, gilt der Eintrag, der dem Kunden bzw. dem zuletzt
    /// abgeglichenen Stand entspricht, sonst der erste.
    /// </summary>
    public static Dictionary<string, string?> Remote(LexwareContact contact, IReadOnlyDictionary<string, string?> local,
        IReadOnlyDictionary<string, string?>? baseline)
    {
        var name = contact.IsCompany
            ? contact.Name
            : string.Join(' ', new[] { contact.FirstName, contact.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var result = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [LexwareSyncFields.Name] = Clean(name),
            [LexwareSyncFields.Street] = contact.HasBillingAddress ? Clean(contact.Street) : null,
            [LexwareSyncFields.PostalCode] = contact.HasBillingAddress ? Clean(contact.PostalCode) : null,
            [LexwareSyncFields.City] = contact.HasBillingAddress ? Clean(contact.City) : null,
            [LexwareSyncFields.Email] = Counterpart(LexwareSyncFields.Email, contact.Emails, local, baseline),
            [LexwareSyncFields.Phone] = Counterpart(LexwareSyncFields.Phone, contact.Phones, local, baseline),
        };
        if (contact.IsCompany)
        {
            result[LexwareSyncFields.VatId] = Clean(contact.VatId);
        }
        else
        {
            result[LexwareSyncFields.Salutation] = Clean(contact.Salutation);
        }

        return result;
    }

    private static string? Counterpart(string field, IReadOnlyList<string> values, IReadOnlyDictionary<string, string?> local,
        IReadOnlyDictionary<string, string?>? baseline)
    {
        var mine = local.GetValueOrDefault(field);
        var before = baseline?.GetValueOrDefault(field);
        return Clean(values.FirstOrDefault(v => mine is not null && Same(field, v, mine))
            ?? values.FirstOrDefault(v => before is not null && Same(field, v, before))
            ?? (values.Count > 0 ? values[0] : null));
    }

    /// <summary>Werte aus Lexware Office in den Kunden übernehmen (nur die genannten Felder).</summary>
    public static void ApplyToCustomer(Contact contact, IReadOnlyDictionary<string, string?> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        string? Value(string field, string? current, int length) => values.TryGetValue(field, out var value) ? LexwareService.Cut(value, length) : current;
        var billing = BillingInUse(contact);
        var name = Value(LexwareSyncFields.Name, contact.Name, 200) ?? contact.Name;
        var address = billing ? contact.BillingAddress! : new ContactAddress(null, contact.Street, contact.PostalCode, contact.City);
        address = address with
        {
            Street = Value(LexwareSyncFields.Street, address.Street, 200), PostalCode = Value(LexwareSyncFields.PostalCode, address.PostalCode, 10),
            City = Value(LexwareSyncFields.City, address.City, 100),
        };
        contact.Update(contact.Kind, name, contact.CustomerNumber, billing ? contact.Street : address.Street, billing ? contact.PostalCode : address.PostalCode,
            billing ? contact.City : address.City, contact.Phone, contact.Email, contact.Notes);

        var emails = contact.Emails.ToList();
        if (values.TryGetValue(LexwareSyncFields.Email, out var email))
        {
            var label = emails.FirstOrDefault()?.Label;
            emails = emails.Skip(1).Where(e => !Same(LexwareSyncFields.Email, e.Address, email)).ToList();
            if (LexwareService.Cut(email, 320) is { } address0)
            {
                emails.Insert(0, new ContactEmail(address0, label));
            }
        }

        var phones = contact.Phones.ToList();
        if (values.TryGetValue(LexwareSyncFields.Phone, out var phone))
        {
            var index = phones.FindIndex(p => p.Kind != ContactPhoneKind.Fax);
            var old = index >= 0 ? phones[index] : null;
            if (index >= 0)
            {
                phones.RemoveAt(index);
            }

            phones = phones.Where(p => p.Kind == ContactPhoneKind.Fax || !Same(LexwareSyncFields.Phone, p.Number, phone)).ToList();
            if (LexwareService.Cut(phone, 50) is { } number)
            {
                phones.Insert(0, new ContactPhone(old?.Kind ?? ContactPhoneKind.Landline, number, old?.Label));
            }
        }

        contact.UpdateDetails(new ContactDetails(phones, emails, Value(LexwareSyncFields.Salutation, contact.Salutation, 30), contact.Title,
            billing ? address : contact.BillingAddress, contact.DeliveryAddress, Value(LexwareSyncFields.VatId, contact.VatId, 30), contact.TaxNumber,
            contact.PreferredChannel, contact.Birthday, contact.Website));
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string? Serialize(IReadOnlyDictionary<string, string?> baseline) => baseline.Count == 0 ? null : JsonSerializer.Serialize(baseline, Json);

    public static Dictionary<string, string?>? Baseline(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? Serialize(IReadOnlyList<LexwareFieldDeviation> deviations) => deviations.Count == 0 ? null : JsonSerializer.Serialize(deviations, Json);

    public static IReadOnlyList<LexwareFieldDeviation> Deviations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<LexwareFieldDeviation>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
