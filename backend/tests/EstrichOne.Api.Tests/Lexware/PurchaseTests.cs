using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Lexware;
using EstrichOne.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Lexware;

/// <summary>
/// Modul „Eingangsbelege“ gegen die nachgebildete API: Eingangsrechnungen aus der Belegliste, Zuordnung zur Bauakte mit
/// Erlösen, Kosten und Differenz, Lieferanten nach Name und von Hand, Belegeingang mit E-Rechnung, Übergabe an Lexware
/// Office genau einmal (<c>POST /v1/files</c>), Rechte und Modulschalter.
/// </summary>
public sealed class PurchaseTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly FakeLexware _lexware = new();
    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Ubl = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
          xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
          xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cbc:ID>RE-88</cbc:ID><cbc:IssueDate>2026-11-20</cbc:IssueDate><cbc:DueDate>2026-12-04</cbc:DueDate>
          <cbc:DocumentCurrencyCode>EUR</cbc:DocumentCurrencyCode><cbc:BuyerReference>Bauakte</cbc:BuyerReference>
          <cac:AccountingSupplierParty><cac:Party>
            <cac:PostalAddress><cbc:StreetName>Werkstraße 5</cbc:StreetName><cbc:CityName>Rosenheim</cbc:CityName><cbc:PostalZone>83026</cbc:PostalZone></cac:PostalAddress>
            <cac:PartyTaxScheme><cbc:CompanyID>DE123456789</cbc:CompanyID></cac:PartyTaxScheme>
            <cac:PartyLegalEntity><cbc:RegistrationName>Baustoffe Inn GmbH</cbc:RegistrationName></cac:PartyLegalEntity>
          </cac:Party></cac:AccountingSupplierParty>
          <cac:AccountingCustomerParty><cac:Party><cac:PartyLegalEntity><cbc:RegistrationName>Estrichbau Muster GmbH</cbc:RegistrationName></cac:PartyLegalEntity></cac:Party></cac:AccountingCustomerParty>
          <cac:TaxTotal><cbc:TaxAmount currencyID="EUR">19.00</cbc:TaxAmount></cac:TaxTotal>
          <cac:LegalMonetaryTotal><cbc:TaxExclusiveAmount currencyID="EUR">100.00</cbc:TaxExclusiveAmount>
            <cbc:TaxInclusiveAmount currencyID="EUR">119.00</cbc:TaxInclusiveAmount><cbc:PayableAmount currencyID="EUR">119.00</cbc:PayableAmount></cac:LegalMonetaryTotal>
          <cac:InvoiceLine><cbc:InvoicedQuantity unitCode="C62">4</cbc:InvoicedQuantity><cbc:LineExtensionAmount currencyID="EUR">100.00</cbc:LineExtensionAmount>
            <cac:Item><cbc:Name>Randdämmstreifen</cbc:Name><cac:ClassifiedTaxCategory><cbc:Percent>19</cbc:Percent></cac:ClassifiedTaxCategory></cac:Item>
            <cac:Price><cbc:PriceAmount currencyID="EUR">25.00</cbc:PriceAmount></cac:Price></cac:InvoiceLine>
        </Invoice>
        """;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Lexware:MinRequestInterval"] = "00:00:00", ["EstrichOne:Lexware:RetryDelay"] = "00:00:00.020",
            ["EstrichOne:Lexware:BackgroundChangeSync"] = "false", ["EstrichOne:Lexware:StatusCacheDuration"] = "00:00:00",
        }, services => services.AddHttpClient(LexwareClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _lexware));
        _admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
        _lexware.Dispose();
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: TestJson.Options) };
        using var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        return text.Length == 0 ? default! : JsonNode.Parse(text).Deserialize<T>(TestJson.Options)!;
    }

    private static async Task<(HttpStatusCode Status, string Text)> UploadAsync(HttpClient client, byte[] content, string name, Guid? bauakteId = null)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var path = $"/api/v1/purchases/documents?fileName={Uri.EscapeDataString(name)}" + (bauakteId is { } b ? $"&bauakteId={b}" : string.Empty);
        using var response = await client.PostAsync(path, body, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task ConfigureAsync()
    {
        await SendAsync<JsonObject>(_admin, HttpMethod.Put, "/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["lexware"] = true, ["purchases"] = true } });
        await SendAsync<JsonObject>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { apiKey = FakeLexware.Key });
    }

    private async Task<Guid> BauakteAsync()
    {
        var customer = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/contacts", new { id = customer, kind = "company", name = "Huber Bau GmbH" }, HttpStatusCode.Created);
        var id = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/bauakten",
            new { id, title = "Halle Huber", customerId = customer, status = "planned", street = "Gewerbering 3", postalCode = "83026", city = "Rosenheim" },
            HttpStatusCode.Created);
        return id;
    }

    [Fact]
    public async Task PurchaseVouchers_AreListed_Assigned_AndCountAsCostsOfTheBauakte()
    {
        await ConfigureAsync();
        var bauakte = await BauakteAsync();
        var supplier = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Put, $"/api/v1/suppliers/{supplier}", new { id = supplier, name = "Baustoffe Inn GmbH" });
        var vendor = _lexware.AddContact("Baustoffe Inn GmbH", customer: false);
        var purchase = _lexware.AddVoucher("purchaseinvoice", "open", "ER-1", "2026-11-10", "2026-11-20", 1000m, 1190m, vendor, "Baustoffe Inn GmbH");
        var refund = _lexware.AddVoucher("purchasecreditnote", "open", "GS-1", "2026-11-12", null, 100m, 119m, vendor, "Baustoffe Inn GmbH");
        var invoice = _lexware.AddVoucher("invoice", "open", "RE-2026-1", "2026-11-15", "2026-12-15", 3000m, 3570m, null, "Huber Bau GmbH");

        var list = await SendAsync<PurchaseListResponse>(_admin, HttpMethod.Get, "/api/v1/purchases/vouchers?refresh=true");
        list.LexwareError.ShouldBeNull();
        list.Items.Select(i => i.Number).ShouldBe(["GS-1", "ER-1"]);
        var er = list.Items.Single(i => i.Number == "ER-1");
        (er.TypeName, er.Status, er.SupplierId, er.SupplierLinkedByHand).ShouldBe(("Eingangsrechnung", "overdue", (Guid?)supplier, false));
        er.LexwareUrl!.ShouldContain("/permalink/vouchers/view/" + purchase);

        await SendAsync<object>(_admin, HttpMethod.Put, $"/api/v1/purchases/vouchers/{purchase}/bauakte", new { bauakteId = bauakte }, HttpStatusCode.NoContent);
        await SendAsync<object>(_admin, HttpMethod.Put, $"/api/v1/purchases/vouchers/{refund}/bauakte", new { bauakteId = bauakte }, HttpStatusCode.NoContent);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            db.Add(new LexwareVoucherLink
            {
                Id = Guid.CreateVersion7(), BauakteId = bauakte, LexwareId = invoice, VoucherType = "invoice", Number = "RE-2026-1", Status = "open", GrossAmount = 3570m,
                Currency = "EUR",
            });
            await db.SaveChangesAsync(Ct);
        }
        var costs = await SendAsync<BauakteCostsResponse>(_admin, HttpMethod.Get, $"/api/v1/purchases/bauakten/{bauakte}/costs");
        (costs.Revenue, costs.Costs, costs.Difference).ShouldBe((3570m, 1071m, 2499m));
        costs.Text.ShouldBe("Erlöse 3.570,00 €, Kosten 1.071,00 €, Differenz 2.499,00 € (brutto laut Lexware Office).");
        costs.Vouchers.Count.ShouldBe(2);

        // Lieferant von Hand verknüpfen (anderer Name) und Zuordnung lösen.
        var other = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Put, $"/api/v1/suppliers/{other}", new { id = other, name = "Inn Baustoffe Rosenheim" });
        await SendAsync<object>(_admin, HttpMethod.Put, $"/api/v1/purchases/suppliers/{vendor}", new { supplierId = other }, HttpStatusCode.NoContent);
        await SendAsync<object>(_admin, HttpMethod.Put, $"/api/v1/purchases/vouchers/{refund}/bauakte", new { bauakteId = (Guid?)null }, HttpStatusCode.NoContent);
        list = await SendAsync<PurchaseListResponse>(_admin, HttpMethod.Get, "/api/v1/purchases/vouchers");
        er = list.Items.Single(i => i.Number == "ER-1");
        (er.SupplierId, er.SupplierLinkedByHand, er.BauakteId).ShouldBe(((Guid?)other, true, (Guid?)bauakte));
        list.Items.Single(i => i.Number == "GS-1").BauakteId.ShouldBeNull();
        (await SendAsync<BauakteCostsResponse>(_admin, HttpMethod.Get, $"/api/v1/purchases/bauakten/{bauakte}/costs")).Costs.ShouldBe(1190m);

        // Nur gelesen: kein schreibender Aufruf an Lexware Office.
        _lexware.Calls.Where(c => c.Method != "GET").ShouldBeEmpty();
    }

    [Fact]
    public async Task Upload_ReadsTheEInvoice_AndSendsItToLexwareExactlyOnce()
    {
        await ConfigureAsync();
        var bauakte = await BauakteAsync();
        var (status, text) = await UploadAsync(_admin, Encoding.UTF8.GetBytes(Ubl), "rechnung.xml", bauakte);
        status.ShouldBe(HttpStatusCode.Created, text);
        var uploaded = JsonNode.Parse(text).Deserialize<PurchaseDocumentResponse>(TestJson.Options)!;
        (uploaded.Document.Format, uploaded.Document.FormatName, uploaded.Document.SupplierName, uploaded.Document.GrossAmount, uploaded.Document.State)
            .ShouldBe(("xrechnung-ubl", "XRechnung (UBL)", "Baustoffe Inn GmbH", (decimal?)119m, "uploaded"));
        uploaded.Data.Lines.ShouldHaveSingleItem().Name.ShouldBe("Randdämmstreifen");
        uploaded.Data.Problems.ShouldBeEmpty();

        // Dieselbe Datei ein zweites Mal: vorhandener Beleg, kein zweiter.
        (status, text) = await UploadAsync(_admin, Encoding.UTF8.GetBytes(Ubl), "nochmal.xml");
        status.ShouldBe(HttpStatusCode.OK);
        JsonNode.Parse(text)!["duplicate"]!.GetValue<bool>().ShouldBeTrue();

        // Foto ohne E-Rechnung: keine Prüfung; unbekannte Datei: abgelehnt.
        (status, text) = await UploadAsync(_admin, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4], "foto");
        status.ShouldBe(HttpStatusCode.Created, text);
        var photo = JsonNode.Parse(text).Deserialize<PurchaseDocumentResponse>(TestJson.Options)!;
        (photo.Document.FileName, photo.Document.Format, photo.Data.Problems.Count).ShouldBe(("foto.jpg", "none", 0));
        (await UploadAsync(_admin, Encoding.UTF8.GetBytes("MZ ausführbar"), "x.exe")).Status.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await UploadAsync(_admin, new byte[PurchaseDocument.MaxBytes + 1], "gross.pdf")).Status.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        var sent = await SendAsync<PurchaseDocumentResponse>(_admin, HttpMethod.Post, $"/api/v1/purchases/documents/{uploaded.Document.Id}/send");
        sent.Document.State.ShouldBe("sent");
        sent.Document.SentAt.ShouldNotBeNull();
        (await SendAsync<PurchaseDocumentResponse>(_admin, HttpMethod.Post, $"/api/v1/purchases/documents/{uploaded.Document.Id}/send")).Document.State.ShouldBe("sent");
        _lexware.CallsTo("POST", "/v1/files").Count().ShouldBe(1);
        _lexware.Files.Values.Single().ShouldContain("rechnung.xml");

        // Übergebene Belege bleiben; nicht übergebene lassen sich verwerfen.
        await SendAsync<JsonObject>(_admin, HttpMethod.Delete, $"/api/v1/purchases/documents/{uploaded.Document.Id}", expected: HttpStatusCode.Conflict);
        await SendAsync<object>(_admin, HttpMethod.Delete, $"/api/v1/purchases/documents/{photo.Document.Id}", expected: HttpStatusCode.NoContent);

        var costs = await SendAsync<BauakteCostsResponse>(_admin, HttpMethod.Get, $"/api/v1/purchases/bauakten/{bauakte}/costs");
        costs.Documents.ShouldHaveSingleItem().InvoiceNumber.ShouldBe("RE-88");
        using var file = await _admin.GetAsync($"/api/v1/purchases/documents/{uploaded.Document.Id}/file", Ct);
        (await file.Content.ReadAsStringAsync(Ct)).ShouldBe(Ubl);

        // Fehler aus Lexware Office: verständlich, erneut senden möglich.
        (status, text) = await UploadAsync(_admin, Encoding.ASCII.GetBytes("%PDF-1.4\n% zweiter Beleg\n%%EOF\n"), "beleg.pdf");
        var pdf = JsonNode.Parse(text).Deserialize<PurchaseDocumentResponse>(TestJson.Options)!;
        _lexware.Raw["/v1/files"] = (406, """{"status":406,"error":"Not Acceptable","message":"File type not supported"}""");
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, $"/api/v1/purchases/documents/{pdf.Document.Id}/send", expected: HttpStatusCode.FailedDependency);
        (await SendAsync<PurchaseDocumentResponse>(_admin, HttpMethod.Get, $"/api/v1/purchases/documents/{pdf.Document.Id}")).Document.State.ShouldBe("failed");
        _lexware.Raw.Clear();
        (await SendAsync<PurchaseDocumentResponse>(_admin, HttpMethod.Post, $"/api/v1/purchases/documents/{pdf.Document.Id}/send")).Document.State.ShouldBe("sent");
    }

    [Fact]
    public async Task ModuleAndRights_AreEnforced_AndOnlyFilesMayBeWritten()
    {
        await SendAsync<JsonObject>(_admin, HttpMethod.Put, "/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["lexware"] = true } });
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using (var off = await office.GetAsync("/api/v1/purchases/vouchers", Ct))
        {
            off.IsSuccessStatusCode.ShouldBeFalse();
        }

        (await UploadAsync(office, Encoding.UTF8.GetBytes(Ubl), "r.xml")).Status.ShouldNotBe(HttpStatusCode.Created);
        await ConfigureAsync();
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using (var denied = await worker.GetAsync("/api/v1/purchases/vouchers", Ct))
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await UploadAsync(worker, Encoding.UTF8.GetBytes(Ubl), "r.xml")).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await UploadAsync(office, Encoding.UTF8.GetBytes(Ubl), "r.xml")).Status.ShouldBe(HttpStatusCode.Created);
        (await SendAsync<PurchaseListResponse>(office, HttpMethod.Get, "/api/v1/purchases/vouchers")).CanEdit.ShouldBeTrue();

        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/files").ShouldBeTrue();
        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/files", "?type=voucher").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Put, "/v1/files/abc").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/vouchers").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Delete, "/v1/files/abc").ShouldBeFalse();
    }
}
