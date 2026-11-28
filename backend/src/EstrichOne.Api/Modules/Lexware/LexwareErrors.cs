using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>
/// Eine einzelne Beanstandung von Lexware Office, übersetzt: deutscher Text mit Bezug auf Feld bzw. Position und, wo
/// EstrichONE die Ursache kennt, eine direkte Behebung (<see cref="Fix"/>).
/// </summary>
/// <param name="Field">Feld in EstrichONE (z. B. <c>taxType</c>, <c>lines.unit</c>), sonst leer.</param>
/// <param name="Position">Positionsnummer ab 1, wenn sich die Beanstandung auf eine Position bezieht.</param>
/// <param name="Fix">Bekannte Behebung, z. B. <see cref="LexwareErrors.FixTaxTypeNet"/>.</param>
public sealed record LexwareIssue(string Text, string? Field = null, int? Position = null, string? Fix = null);

/// <summary>
/// Übersetzt die Fehlerantworten von Lexware Office (400, 406, 422) in verständliche deutsche Meldungen. Gelesen werden das
/// ältere Format (<c>message</c>, <c>details[].field/message/violation</c>) und das neuere (<c>IssueList[].source/type/i18nKey</c>).
/// Der englische Originaltext bleibt als technische Einzelheit erhalten. Reine Funktionen (mit Unit-Tests geprüft).
/// </summary>
public static partial class LexwareErrors
{
    /// <summary>Behebung: Steuerart Netto übernehmen und erneut senden.</summary>
    public const string FixTaxTypeNet = "taxTypeNet";

    /// <summary>Behebung: Steuerart Steuerfrei übernehmen (Kleinunternehmer).</summary>
    public const string FixTaxTypeVatfree = "taxTypeVatfree";

    /// <summary>Code der abgelehnten Steuerart.</summary>
    public const string TaxTypeNotAllowed = "TAX_TYPE_NOT_ALLOWED";

    public const string Generic = "Lexware Office hat die Angaben nicht angenommen.";

    /// <summary>Steuerarten der Lexware-API mit deutscher Bezeichnung.</summary>
    public static string TaxTypeName(string? taxType) => taxType switch
    {
        "net" => "Netto",
        "gross" => "Brutto",
        "vatfree" => "Steuerfrei",
        "constructionService13b" => "Bauleistung nach § 13b UStG",
        "externalService13b" => "Fremdleistung nach § 13b UStG",
        "intraCommunitySupply" => "Innergemeinschaftliche Lieferung",
        "thirdPartyCountryService" => "Leistung in ein Drittland",
        "thirdPartyCountryDelivery" => "Lieferung in ein Drittland",
        "photovoltaicEquipment" => "Photovoltaikanlage (0 Prozent)",
        null or "" => "unbekannt",
        _ => taxType,
    };

    /// <summary>Meldung, wenn Lexware Office eine Steuerart für die Organisation nicht zulässt.</summary>
    /// <param name="voucher">Belegart im Akkusativ mit Artikel, z. B. „das Angebot“, „die Rechnung“.</param>
    public static string TaxTypeText(string? taxType, string voucher = "den Beleg") =>
        taxType == "net"
            ? $"Lexware Office lässt die Steuerart Netto für Ihr Unternehmen nicht zu. Prüfen Sie die Steuereinstellungen in Lexware Office (zum Beispiel Kleinunternehmerregelung) und senden Sie {voucher} mit der Steuerart Steuerfrei."
            : $"Lexware Office lässt die Steuerart {TaxTypeName(taxType)} für Ihr Unternehmen nicht zu. Prüfen Sie die Steuereinstellungen in Lexware Office oder senden Sie {voucher} mit der Steuerart Netto.";

    /// <summary>Ergebnis der Übersetzung.</summary>
    /// <param name="Code"><c>INVALID</c> oder <see cref="TaxTypeNotAllowed"/>.</param>
    /// <param name="RejectedTaxType">Von Lexware Office abgelehnte Steuerart (falls erkannt).</param>
    /// <param name="Technical">Originaltext von Lexware Office (englisch, gekürzt).</param>
    public sealed record Translation(string Code, string Message, IReadOnlyList<LexwareIssue> Issues, string? Technical, string? RejectedTaxType);

    private sealed record RawIssue(string? Field, string? Text);

    public static Translation Translate(string? body)
    {
        var technical = LexwareClient.Details(body);
        var raw = Read(body);
        var issues = new List<LexwareIssue>();
        string? rejected = null;
        foreach (var item in raw)
        {
            if (item.Text is { } text && TaxTypePattern().Match(text) is { Success: true } match)
            {
                rejected ??= match.Groups["type"].Value;
                var type = match.Groups["type"].Value;
                issues.Add(new LexwareIssue(TaxTypeText(type), "taxType", null, type == "net" ? FixTaxTypeVatfree : FixTaxTypeNet));
                continue;
            }

            if (Issue(item) is { } issue && !issues.Any(i => i.Text == issue.Text))
            {
                issues.Add(issue);
            }
        }

        if (rejected is not null)
        {
            return new Translation(TaxTypeNotAllowed, issues.First(i => i.Field == "taxType").Text, issues, Blank(technical), rejected);
        }

        var message = issues.Count == 0 ? Generic : Generic + " " + string.Join(" ", issues.Select(i => i.Text));
        return new Translation("INVALID", message.Length > 1500 ? message[..1500] : message, issues, Blank(technical), null);
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static List<RawIssue> Read(string? body)
    {
        var list = new List<RawIssue>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return list;
        }

        JsonNode? json;
        try
        {
            json = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return list;
        }

        foreach (var detail in (json?["details"] as JsonArray ?? []).OfType<JsonObject>())
        {
            list.Add(new RawIssue(Str(detail["field"]), Str(detail["message"]) ?? Str(detail["violation"])));
        }

        foreach (var issue in (json?["IssueList"] as JsonArray ?? []).OfType<JsonObject>())
        {
            list.Add(new RawIssue(Str(issue["source"]), Str(issue["i18nKey"]) ?? Str(issue["type"])));
        }

        // Die Hauptmeldung zählt, wenn es keine Einzelangaben gibt oder sie eine eigene Aussage hat (z. B. Steuerart).
        if (Str(json?["message"]) is { } message && (list.Count == 0 || TaxTypePattern().IsMatch(message)))
        {
            list.Add(new RawIssue(null, message));
        }

        return list;
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static LexwareIssue? Issue(RawIssue raw)
    {
        var (label, field, position) = Field(raw.Field);
        var phrase = Phrase(raw.Text);
        if (label is null && phrase is null)
        {
            return null;
        }

        var text = (label, phrase) switch
        {
            ({ } l, { } p) => $"{l} {p}.",
            ({ } l, null) => $"{l} wurde nicht angenommen.",
            (null, { } p) => $"Eine Angabe {p}.",
            _ => Generic,
        };
        if (position is { } number)
        {
            text = $"Position {number.ToString(CultureInfo.InvariantCulture)}: {text}";
        }

        return new LexwareIssue(text, field, position);
    }

    /// <summary>Feldpfad der API als Bezeichnung in der Maske, Feld in EstrichONE und Positionsnummer.</summary>
    internal static (string? Label, string? Field, int? Position) Field(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return (null, null, null);
        }

        var match = LinePattern().Match(path);
        if (match.Success)
        {
            var index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
            var (label, field) = match.Groups["rest"].Value switch
            {
                "name" => ("Die Bezeichnung", "lines.name"),
                "description" => ("Die Beschreibung", "lines.description"),
                "quantity" => ("Die Menge", "lines.quantity"),
                "unitName" => ("Die Einheit", "lines.unit"),
                "unitPrice.netAmount" or "unitPrice" => ("Der Einzelpreis", "lines.price"),
                "unitPrice.grossAmount" => ("Der Bruttopreis", "lines.price"),
                "unitPrice.taxRatePercentage" => ("Der Steuersatz", "lines.tax"),
                "discountPercentage" => ("Der Rabatt", "lines.discount"),
                "id" => ("Der Artikel aus Lexware Office", "lines.article"),
                "type" => ("Die Art der Position", "lines.kind"),
                _ => ("Eine Angabe", null),
            };
            return (label, field, index + 1);
        }

        return path switch
        {
            "voucherDate" => ("Das Belegdatum", "voucherDate", null),
            "expirationDate" => ("Das Datum „Gültig bis“", "validUntil", null),
            "address" or "address.contactId" => ("Der Kunde (Kontakt in Lexware Office)", "customer", null),
            "address.name" => ("Der Name in der Anschrift", "customer", null),
            "title" => ("Der Titel", "title", null),
            "introduction" => ("Der Einleitungstext", "introduction", null),
            "remark" => ("Der Schlusstext", "remark", null),
            "taxConditions" or "taxConditions.taxType" => ("Die Steuerart", "taxType", null),
            "lineItems" => ("Die Positionen", "lines", null),
            "totalPrice" or "totalPrice.currency" => ("Die Währung", null, null),
            "shippingConditions" or "shippingConditions.shippingType" => ("Die Art des Leistungsdatums", "serviceDate", null),
            "shippingConditions.shippingDate" => ("Das Leistungsdatum", "serviceDate", null),
            "shippingConditions.shippingEndDate" => ("Das Ende des Leistungszeitraums", "serviceEndDate", null),
            "precedingSalesVoucherId" => ("Der vorhergehende Beleg", "preceding", null),
            "company.name" => ("Der Firmenname", "name", null),
            "person.lastName" => ("Der Nachname", "name", null),
            "person.firstName" => ("Der Vorname", "name", null),
            "person.salutation" => ("Die Anrede", "salutation", null),
            "company.vatRegistrationId" => ("Die USt-IdNr.", "vatId", null),
            "company.taxNumber" => ("Die Steuernummer", "taxNumber", null),
            _ when path.StartsWith("addresses.", StringComparison.Ordinal) && path.EndsWith(".zip", StringComparison.Ordinal) => ("Die Postleitzahl der Anschrift", "postalCode", null),
            _ when path.StartsWith("addresses.", StringComparison.Ordinal) && path.EndsWith(".street", StringComparison.Ordinal) => ("Die Straße der Anschrift", "street", null),
            _ when path.StartsWith("addresses.", StringComparison.Ordinal) && path.EndsWith(".city", StringComparison.Ordinal) => ("Der Ort der Anschrift", "city", null),
            _ when path.StartsWith("addresses.", StringComparison.Ordinal) && path.EndsWith(".countryCode", StringComparison.Ordinal) => ("Das Land der Anschrift", "country", null),
            _ when path.StartsWith("emailAddresses", StringComparison.Ordinal) => ("Die E-Mail-Adresse", "email", null),
            _ when path.StartsWith("phoneNumbers", StringComparison.Ordinal) => ("Die Telefonnummer", "phone", null),
            _ when path.StartsWith("paymentConditions", StringComparison.Ordinal) => ("Die Zahlungsbedingung", "payment", null),
            _ => (null, null, null),
        };
    }

    /// <summary>Englische Fehlerbeschreibung bzw. Schlüssel als deutsche Aussage (ohne Satzzeichen am Ende).</summary>
    internal static string? Phrase(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.ToLowerInvariant();
        return t switch
        {
            _ when t.Contains("missing", StringComparison.Ordinal) || t.Contains("must not be null", StringComparison.Ordinal)
                || t.Contains("must not be empty", StringComparison.Ordinal) || t.Contains("must not be blank", StringComparison.Ordinal)
                || t.Contains("notnull", StringComparison.Ordinal) || t.Contains("notempty", StringComparison.Ordinal)
                || t.Contains("darf nicht leer", StringComparison.Ordinal) || t.Contains("required", StringComparison.Ordinal) => "fehlt",
            _ when t.Contains("not allowed", StringComparison.Ordinal) || t.Contains("not_allowed", StringComparison.Ordinal)
                || t.Contains("not permitted", StringComparison.Ordinal) => "ist in Lexware Office nicht zulässig",
            _ when t.Contains("not found", StringComparison.Ordinal) || t.Contains("not_found", StringComparison.Ordinal)
                || t.Contains("does not exist", StringComparison.Ordinal) => "wurde in Lexware Office nicht gefunden",
            _ when t.Contains("too long", StringComparison.Ordinal) || t.Contains("size must", StringComparison.Ordinal)
                || t.Contains("length", StringComparison.Ordinal) => "ist zu lang",
            _ when t.Contains("greater than", StringComparison.Ordinal) || t.Contains("positive", StringComparison.Ordinal)
                || t.Contains("negative", StringComparison.Ordinal) || t.Contains("minimum", StringComparison.Ordinal)
                || t.Contains("at least", StringComparison.Ordinal) => "ist zu klein oder negativ",
            _ when t.Contains("less than", StringComparison.Ordinal) || t.Contains("maximum", StringComparison.Ordinal)
                || t.Contains("at most", StringComparison.Ordinal) => "ist zu groß",
            _ when t.Contains("date", StringComparison.Ordinal) && (t.Contains("past", StringComparison.Ordinal) || t.Contains("future", StringComparison.Ordinal)
                || t.Contains("before", StringComparison.Ordinal) || t.Contains("after", StringComparison.Ordinal)) => "liegt in einem nicht zulässigen Zeitraum",
            _ when t.Contains("invalid", StringComparison.Ordinal) || t.Contains("not valid", StringComparison.Ordinal) || t.Contains("ungültig", StringComparison.Ordinal) || t.Contains("must match", StringComparison.Ordinal)
                || t.Contains("format", StringComparison.Ordinal) || t.Contains("validation", StringComparison.Ordinal)
                || t.Contains("unsupported", StringComparison.Ordinal) => "ist ungültig",
            _ => null,
        };
    }

    [GeneratedRegex(@"tax\s*type\s*'?(?<type>[A-Za-z0-9]+)'?\s+(is\s+)?not\s+(allowed|permitted|supported)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TaxTypePattern();

    [GeneratedRegex(@"^lineItems\[(?<index>\d+)\]\.?(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LinePattern();
}
