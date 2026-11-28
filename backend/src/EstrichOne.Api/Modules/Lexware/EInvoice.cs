using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Position einer E-Rechnung.</summary>
public sealed record EInvoiceLine(string Name, decimal? Quantity, string? Unit, decimal? NetPrice, decimal? LineNet, decimal? TaxRate);

/// <summary>
/// Strukturierte Daten einer E-Rechnung (XRechnung als UBL oder CII, ZUGFeRD bzw. Factur-X als PDF mit eingebetteter CII-Datei).
/// </summary>
/// <param name="Format"><c>xrechnung-ubl</c>, <c>xrechnung-cii</c>, <c>zugferd</c> oder <c>none</c> (kein strukturierter Inhalt).</param>
/// <param name="BuyerReference">Leitweg-ID bzw. Käuferreferenz (BT-10).</param>
public sealed record EInvoiceData(
    string Format, bool CreditNote, string? Number, DateOnly? IssueDate, DateOnly? DueDate, string? SellerName, string? SellerVatId, string? SellerStreet,
    string? SellerPostalCode, string? SellerCity, string? BuyerName, string? BuyerReference, string Currency, decimal? NetTotal, decimal? TaxTotal,
    decimal? GrossTotal, decimal? DuePayable, IReadOnlyList<EInvoiceLine> Lines, IReadOnlyList<string> Problems)
{
    public static EInvoiceData None(string? problem = null) => new("none", false, null, null, null, null, null, null, null, null, null, null, "EUR", null, null, null,
        null, [], problem is null ? [] : [problem]);
}

/// <summary>
/// Eigener Leser für E-Rechnungen ohne Cloud-Dienst: XRechnung (UBL 2.1 Invoice und CreditNote, UN/CEFACT CII) und
/// ZUGFeRD bzw. Factur-X (CII-Datei als eingebetteter, meist komprimierter Datenstrom im PDF). Gelesen wird über lokale
/// Elementnamen, damit Namensraumpräfixe keine Rolle spielen. Externe Entitäten sind abgeschaltet. Dazu eine Prüfung der
/// Pflichtangaben mit verständlichen Hinweisen. Reine Funktionen (mit Unit-Tests geprüft).
/// </summary>
public static class EInvoiceReader
{
    public const int MaxBytes = 20 * 1024 * 1024;

    public static EInvoiceData Read(byte[] content)
    {
        if (content.Length == 0 || content.Length > MaxBytes)
        {
            return EInvoiceData.None("Die Datei ist leer oder zu groß.");
        }

        if (content.Length > 4 && content[0] == (byte)'%' && content[1] == (byte)'P' && content[2] == (byte)'D' && content[3] == (byte)'F')
        {
            return FromPdf(content);
        }

        var xml = TryParse(content);
        return xml is null ? EInvoiceData.None() : FromXml(xml, null);
    }

    private static XDocument? TryParse(byte[] content)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes };
            using var reader = XmlReader.Create(new MemoryStream(content), settings);
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>ZUGFeRD bzw. Factur-X: Datenströme des PDF durchsuchen (unkomprimiert oder FlateDecode) und die CII-Datei lesen.</summary>
    private static EInvoiceData FromPdf(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var index = 0;
        while ((index = text.IndexOf("stream", index, StringComparison.Ordinal)) >= 0)
        {
            var start = index + 6;
            if (start < text.Length && text[start] == '\r')
            {
                start++;
            }

            if (start < text.Length && text[start] == '\n')
            {
                start++;
            }

            var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0)
            {
                break;
            }

            index = end + 9;
            var data = pdf.AsSpan(start, end - start).ToArray();
            foreach (var candidate in new[] { data, Inflate(data) })
            {
                if (candidate is { Length: > 0 } && Encoding.UTF8.GetString(candidate).Contains("CrossIndustryInvoice", StringComparison.Ordinal)
                    && TryParse(Trim(candidate)) is { } xml)
                {
                    return FromXml(xml, "zugferd");
                }
            }
        }

        return EInvoiceData.None("Das PDF enthält keine strukturierten Rechnungsdaten (kein ZUGFeRD bzw. Factur-X). Es wird als Beleg ohne E-Rechnung behandelt.");
    }

    private static byte[] Trim(byte[] data)
    {
        var s = Encoding.UTF8.GetString(data);
        var first = s.IndexOf('<');
        var last = s.LastIndexOf('>');
        return first >= 0 && last > first ? Encoding.UTF8.GetBytes(s[first..(last + 1)]) : data;
    }

    private static byte[] Inflate(byte[] data)
    {
        try
        {
            using var input = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxBytes)
                {
                    return [];
                }
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    private static IEnumerable<XElement> All(XContainer node, string name) => node.Descendants().Where(e => e.Name.LocalName == name);

    private static XElement? First(XContainer? node, params string[] path)
    {
        XContainer? current = node;
        foreach (var part in path)
        {
            current = current?.Elements().FirstOrDefault(e => e.Name.LocalName == part);
            if (current is null)
            {
                return null;
            }
        }

        return current as XElement;
    }

    private static string? Text(XContainer? node, params string[] path) =>
        First(node, path)?.Value.Trim() is { Length: > 0 } value ? value : null;

    private static decimal? Amount(XContainer? node, params string[] path) =>
        Text(node, path) is { } text && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateOnly? Date(string? text) => text switch
    {
        null => null,
        _ when DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso) => iso,
        _ when DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact) => compact,
        _ => null,
    };

    private static EInvoiceData FromXml(XDocument xml, string? format)
    {
        var root = xml.Root!;
        return root.Name.LocalName switch
        {
            "Invoice" or "CreditNote" => Ubl(root),
            "CrossIndustryInvoice" => Cii(root, format ?? "xrechnung-cii"),
            _ => EInvoiceData.None("Die XML-Datei ist keine bekannte E-Rechnung (XRechnung UBL oder CII)."),
        };
    }

    private static EInvoiceData Ubl(XElement root)
    {
        var credit = root.Name.LocalName == "CreditNote";
        var seller = First(root, "AccountingSupplierParty", "Party");
        var buyer = First(root, "AccountingCustomerParty", "Party");
        var totals = First(root, "LegalMonetaryTotal");
        var lines = All(root, credit ? "CreditNoteLine" : "InvoiceLine").Select(l => new EInvoiceLine(
            Text(l, "Item", "Name") ?? "Position", Amount(l, credit ? "CreditedQuantity" : "InvoicedQuantity"),
            First(l, credit ? "CreditedQuantity" : "InvoicedQuantity")?.Attribute("unitCode")?.Value, Amount(l, "Price", "PriceAmount"),
            Amount(l, "LineExtensionAmount"), Amount(l, "Item", "ClassifiedTaxCategory", "Percent"))).ToList();
        var data = new EInvoiceData("xrechnung-ubl", credit, Text(root, "ID"), Date(Text(root, "IssueDate")),
            Date(Text(root, "DueDate")) ?? Date(Text(root, "PaymentMeans", "PaymentDueDate")),
            Text(seller, "PartyLegalEntity", "RegistrationName") ?? Text(seller, "PartyName", "Name"), Text(seller, "PartyTaxScheme", "CompanyID"),
            Text(seller, "PostalAddress", "StreetName"), Text(seller, "PostalAddress", "PostalZone"), Text(seller, "PostalAddress", "CityName"),
            Text(buyer, "PartyLegalEntity", "RegistrationName") ?? Text(buyer, "PartyName", "Name"), Text(root, "BuyerReference"),
            Text(root, "DocumentCurrencyCode") ?? "EUR", Amount(totals, "TaxExclusiveAmount"), Amount(root, "TaxTotal", "TaxAmount"),
            Amount(totals, "TaxInclusiveAmount"), Amount(totals, "PayableAmount"), lines, []);
        return data with { Problems = Check(data) };
    }

    private static EInvoiceData Cii(XElement root, string format)
    {
        var document = First(root, "ExchangedDocument");
        var transaction = First(root, "SupplyChainTradeTransaction");
        var agreement = First(transaction, "ApplicableHeaderTradeAgreement");
        var settlement = First(transaction, "ApplicableHeaderTradeSettlement");
        var seller = First(agreement, "SellerTradeParty");
        var buyer = First(agreement, "BuyerTradeParty");
        var sums = First(settlement, "SpecifiedTradeSettlementHeaderMonetarySummation");
        var lines = All(transaction ?? root, "IncludedSupplyChainTradeLineItem").Select(l => new EInvoiceLine(
            Text(l, "SpecifiedTradeProduct", "Name") ?? "Position", Amount(l, "SpecifiedLineTradeDelivery", "BilledQuantity"),
            First(l, "SpecifiedLineTradeDelivery", "BilledQuantity")?.Attribute("unitCode")?.Value,
            Amount(l, "SpecifiedLineTradeAgreement", "NetPriceProductTradePrice", "ChargeAmount"),
            Amount(l, "SpecifiedLineTradeSettlement", "SpecifiedTradeSettlementLineMonetarySummation", "LineTotalAmount"),
            Amount(l, "SpecifiedLineTradeSettlement", "ApplicableTradeTax", "RateApplicablePercent"))).ToList();
        var typeCode = Text(document, "TypeCode");
        var data = new EInvoiceData(format, typeCode == "381", Text(document, "ID"), Date(Text(document, "IssueDateTime", "DateTimeString")),
            Date(Text(settlement, "SpecifiedTradePaymentTerms", "DueDateDateTime", "DateTimeString")), Text(seller, "Name"),
            All(seller ?? root, "SpecifiedTaxRegistration").Select(r => Text(r, "ID")).FirstOrDefault(v => v is not null),
            Text(seller, "PostalTradeAddress", "LineOne"), Text(seller, "PostalTradeAddress", "PostcodeCode"), Text(seller, "PostalTradeAddress", "CityName"),
            Text(buyer, "Name"), Text(agreement, "BuyerReference"), Text(settlement, "InvoiceCurrencyCode") ?? "EUR", Amount(sums, "TaxBasisTotalAmount"),
            Amount(sums, "TaxTotalAmount"), Amount(sums, "GrandTotalAmount"), Amount(sums, "DuePayableAmount"), lines, []);
        return data with { Problems = Check(data) };
    }

    /// <summary>Pflichtangaben und Rechenprüfung als verständliche Hinweise (leer: in Ordnung).</summary>
    public static IReadOnlyList<string> Check(EInvoiceData data)
    {
        var problems = new List<string>();
        if (data.Number is null)
        {
            problems.Add("Die Rechnungsnummer fehlt.");
        }

        if (data.IssueDate is null)
        {
            problems.Add("Das Rechnungsdatum fehlt.");
        }

        if (data.SellerName is null)
        {
            problems.Add("Der Name des Lieferanten fehlt.");
        }

        if (data.SellerVatId is null)
        {
            problems.Add("USt-IdNr. bzw. Steuernummer des Lieferanten fehlt. Bitte prüfen Sie den Beleg vor dem Buchen.");
        }

        if (data.GrossTotal is null)
        {
            problems.Add("Der Gesamtbetrag fehlt.");
        }

        if (data.Lines.Count == 0)
        {
            problems.Add("Die Rechnung enthält keine Positionen.");
        }

        if (data.NetTotal is { } net && data.Lines.Count > 0 && data.Lines.All(l => l.LineNet is not null)
            && Math.Abs(data.Lines.Sum(l => l.LineNet!.Value) - net) > 0.01m)
        {
            problems.Add("Die Summe der Positionen weicht vom Nettobetrag der Rechnung ab (Zu- oder Abschläge auf Belegebene?).");
        }

        if (data.NetTotal is { } n && data.TaxTotal is { } t && data.GrossTotal is { } g && Math.Abs(n + t - g) > 0.01m)
        {
            problems.Add("Nettobetrag und Umsatzsteuer ergeben nicht den Gesamtbetrag.");
        }

        return problems;
    }
}
