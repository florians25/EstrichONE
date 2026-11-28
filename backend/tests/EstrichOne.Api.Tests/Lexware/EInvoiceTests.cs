using System.IO.Compression;
using System.Text;
using EstrichOne.Api.Modules.Lexware;

namespace EstrichOne.Api.Tests.Lexware;

/// <summary>E-Rechnungen lesen (selbst erzeugte Testdateien, keine echten Kundendaten): XRechnung UBL und CII, ZUGFeRD-PDF, Prüfung der Pflichtangaben.</summary>
public sealed class EInvoiceTests
{
    private const string Ubl = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ubl:Invoice xmlns:ubl="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
          xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
          xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cbc:ID>ER-2026-117</cbc:ID>
          <cbc:IssueDate>2026-11-20</cbc:IssueDate>
          <cbc:DueDate>2026-12-04</cbc:DueDate>
          <cbc:DocumentCurrencyCode>EUR</cbc:DocumentCurrencyCode>
          <cbc:BuyerReference>04011000-12345-67</cbc:BuyerReference>
          <cac:AccountingSupplierParty><cac:Party>
            <cac:PostalAddress><cbc:StreetName>Werkstraße 5</cbc:StreetName><cbc:CityName>Rosenheim</cbc:CityName><cbc:PostalZone>83026</cbc:PostalZone></cac:PostalAddress>
            <cac:PartyTaxScheme><cbc:CompanyID>DE123456789</cbc:CompanyID></cac:PartyTaxScheme>
            <cac:PartyLegalEntity><cbc:RegistrationName>Baustoffhandel Muster GmbH</cbc:RegistrationName></cac:PartyLegalEntity>
          </cac:Party></cac:AccountingSupplierParty>
          <cac:AccountingCustomerParty><cac:Party><cac:PartyLegalEntity><cbc:RegistrationName>Estrichbau Beispiel GmbH</cbc:RegistrationName></cac:PartyLegalEntity></cac:Party></cac:AccountingCustomerParty>
          <cac:TaxTotal><cbc:TaxAmount currencyID="EUR">190.00</cbc:TaxAmount></cac:TaxTotal>
          <cac:LegalMonetaryTotal>
            <cbc:TaxExclusiveAmount currencyID="EUR">1000.00</cbc:TaxExclusiveAmount>
            <cbc:TaxInclusiveAmount currencyID="EUR">1190.00</cbc:TaxInclusiveAmount>
            <cbc:PayableAmount currencyID="EUR">1190.00</cbc:PayableAmount>
          </cac:LegalMonetaryTotal>
          <cac:InvoiceLine>
            <cbc:InvoicedQuantity unitCode="TNE">20</cbc:InvoicedQuantity>
            <cbc:LineExtensionAmount currencyID="EUR">1000.00</cbc:LineExtensionAmount>
            <cac:Item><cbc:Name>Zement CEM II 25 kg</cbc:Name><cac:ClassifiedTaxCategory><cbc:Percent>19</cbc:Percent></cac:ClassifiedTaxCategory></cac:Item>
            <cac:Price><cbc:PriceAmount currencyID="EUR">50.00</cbc:PriceAmount></cac:Price>
          </cac:InvoiceLine>
        </ubl:Invoice>
        """;

    private const string Cii = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rsm:CrossIndustryInvoice xmlns:rsm="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100"
          xmlns:ram="urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100"
          xmlns:udt="urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100">
          <rsm:ExchangedDocument><ram:ID>4711</ram:ID><ram:TypeCode>380</ram:TypeCode>
            <ram:IssueDateTime><udt:DateTimeString format="102">20261115</udt:DateTimeString></ram:IssueDateTime></rsm:ExchangedDocument>
          <rsm:SupplyChainTradeTransaction>
            <ram:IncludedSupplyChainTradeLineItem>
              <ram:SpecifiedTradeProduct><ram:Name>Pumpeneinsatz</ram:Name></ram:SpecifiedTradeProduct>
              <ram:SpecifiedLineTradeAgreement><ram:NetPriceProductTradePrice><ram:ChargeAmount>150.00</ram:ChargeAmount></ram:NetPriceProductTradePrice></ram:SpecifiedLineTradeAgreement>
              <ram:SpecifiedLineTradeDelivery><ram:BilledQuantity unitCode="HUR">4</ram:BilledQuantity></ram:SpecifiedLineTradeDelivery>
              <ram:SpecifiedLineTradeSettlement><ram:ApplicableTradeTax><ram:RateApplicablePercent>19</ram:RateApplicablePercent></ram:ApplicableTradeTax>
                <ram:SpecifiedTradeSettlementLineMonetarySummation><ram:LineTotalAmount>600.00</ram:LineTotalAmount></ram:SpecifiedTradeSettlementLineMonetarySummation></ram:SpecifiedLineTradeSettlement>
            </ram:IncludedSupplyChainTradeLineItem>
            <ram:ApplicableHeaderTradeAgreement><ram:BuyerReference>Bauakte 2026-041</ram:BuyerReference>
              <ram:SellerTradeParty><ram:Name>Pumpendienst Nord GmbH</ram:Name>
                <ram:PostalTradeAddress><ram:PostcodeCode>80331</ram:PostcodeCode><ram:LineOne>Hafenweg 2</ram:LineOne><ram:CityName>München</ram:CityName></ram:PostalTradeAddress>
                <ram:SpecifiedTaxRegistration><ram:ID schemeID="VA">DE987654321</ram:ID></ram:SpecifiedTaxRegistration></ram:SellerTradeParty>
              <ram:BuyerTradeParty><ram:Name>Estrichbau Beispiel GmbH</ram:Name></ram:BuyerTradeParty></ram:ApplicableHeaderTradeAgreement>
            <ram:ApplicableHeaderTradeSettlement><ram:InvoiceCurrencyCode>EUR</ram:InvoiceCurrencyCode>
              <ram:SpecifiedTradePaymentTerms><ram:DueDateDateTime><udt:DateTimeString format="102">20261129</udt:DateTimeString></ram:DueDateDateTime></ram:SpecifiedTradePaymentTerms>
              <ram:SpecifiedTradeSettlementHeaderMonetarySummation><ram:TaxBasisTotalAmount>600.00</ram:TaxBasisTotalAmount><ram:TaxTotalAmount currencyID="EUR">114.00</ram:TaxTotalAmount>
                <ram:GrandTotalAmount>714.00</ram:GrandTotalAmount><ram:DuePayableAmount>714.00</ram:DuePayableAmount></ram:SpecifiedTradeSettlementHeaderMonetarySummation>
            </ram:ApplicableHeaderTradeSettlement>
          </rsm:SupplyChainTradeTransaction>
        </rsm:CrossIndustryInvoice>
        """;

    [Fact]
    public void XRechnungUbl_IsRead_WithLeitwegId_AndNoProblems()
    {
        var data = EInvoiceReader.Read(Encoding.UTF8.GetBytes(Ubl));
        data.Format.ShouldBe("xrechnung-ubl");
        (data.Number, data.IssueDate, data.DueDate).ShouldBe(("ER-2026-117", new DateOnly(2026, 11, 20), new DateOnly(2026, 12, 4)));
        data.SellerName.ShouldBe("Baustoffhandel Muster GmbH");
        data.SellerVatId.ShouldBe("DE123456789");
        data.BuyerReference.ShouldBe("04011000-12345-67");
        (data.NetTotal, data.TaxTotal, data.GrossTotal).ShouldBe((1000m, 190m, 1190m));
        data.Lines.ShouldHaveSingleItem().ShouldBe(new EInvoiceLine("Zement CEM II 25 kg", 20m, "TNE", 50m, 1000m, 19m));
        data.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void XRechnungCii_AndZugferdPdf_AreRead()
    {
        var cii = EInvoiceReader.Read(Encoding.UTF8.GetBytes(Cii));
        cii.Format.ShouldBe("xrechnung-cii");
        (cii.Number, cii.IssueDate, cii.DueDate).ShouldBe(("4711", new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 29)));
        cii.SellerName.ShouldBe("Pumpendienst Nord GmbH");
        cii.GrossTotal.ShouldBe(714m);
        cii.Lines.ShouldHaveSingleItem().Unit.ShouldBe("HUR");
        cii.Problems.ShouldBeEmpty();

        // ZUGFeRD: CII-Datei als komprimierter Datenstrom in einem PDF.
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.UTF8.GetBytes(Cii));
        }

        var pdf = new MemoryStream();
        pdf.Write(Encoding.Latin1.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /EmbeddedFile /Filter /FlateDecode >>\nstream\n"));
        pdf.Write(compressed.ToArray());
        pdf.Write(Encoding.Latin1.GetBytes("\nendstream\nendobj\n%%EOF\n"));
        var zugferd = EInvoiceReader.Read(pdf.ToArray());
        zugferd.Format.ShouldBe("zugferd");
        zugferd.Number.ShouldBe("4711");
    }

    [Fact]
    public void Problems_AreExplained_ForIncompleteOrInconsistentInvoices()
    {
        var broken = Ubl.Replace("<cbc:ID>ER-2026-117</cbc:ID>", string.Empty, StringComparison.Ordinal)
            .Replace("<cbc:TaxInclusiveAmount currencyID=\"EUR\">1190.00</cbc:TaxInclusiveAmount>", "<cbc:TaxInclusiveAmount currencyID=\"EUR\">1200.00</cbc:TaxInclusiveAmount>",
                StringComparison.Ordinal);
        var data = EInvoiceReader.Read(Encoding.UTF8.GetBytes(broken));
        data.Problems.ShouldContain("Die Rechnungsnummer fehlt.");
        data.Problems.ShouldContain("Nettobetrag und Umsatzsteuer ergeben nicht den Gesamtbetrag.");
        EInvoiceReader.Read(Encoding.Latin1.GetBytes("%PDF-1.4\nstream\nkein xml\nendstream\n")).Format.ShouldBe("none");
        EInvoiceReader.Read(Encoding.UTF8.GetBytes("<html/>")).Problems.ShouldContain(p => p.Contains("keine bekannte E-Rechnung", StringComparison.Ordinal));
        EInvoiceReader.Read(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><Invoice>&e;</Invoice>")).Format.ShouldBe("none");
    }
}
