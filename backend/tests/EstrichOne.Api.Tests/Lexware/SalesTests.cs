using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Lexware;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Lexware;

/// <summary>
/// Modul „Verkauf“ gegen die nachgebildete API: alle vier Belegarten als Entwurf, Festschreiben nur mit Modul, Recht und
/// Bestätigung, Belegkette über <c>precedingSalesVoucherId</c>, Rechnungsplan der Bauakte mit Abschlägen und Schlussrechnung
/// (Abzugszeilen), Übersicht aus der Belegliste, Rechte je Belegart und die Schreibsperre des Clients.
/// </summary>
public sealed class SalesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly FakeLexware _lexware = new();
    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private static async Task<string> ProblemAsync(HttpClient client, HttpMethod method, string path, HttpStatusCode expected, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: TestJson.Options) };
        using var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        var problem = JsonNode.Parse(text)!;
        return $"{problem["code"]} {problem["detail"]}";
    }

    private Task<JsonObject> ModulesAsync(params string[] keys) =>
        SendAsync<JsonObject>(_admin, HttpMethod.Put, "/api/v1/settings/modules", new { modules = keys.ToDictionary(k => k, _ => true) });

    private async Task ConfigureAsync(params string[] modules)
    {
        await ModulesAsync(["lexware", "sales", "quotations", "salesOrderConfirmations", "salesInvoices", "salesCreditNotes", "salesPayments", .. modules]);
        await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { apiKey = FakeLexware.Key });
    }

    private async Task<(Guid Customer, string LexwareId)> LinkedCustomerAsync(string name = "Huber Bau GmbH")
    {
        var lexwareId = _lexware.AddContact(name, email: $"{Guid.NewGuid():N}@huber.example");
        var id = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/contacts", new { lexwareCreateAnyway = true, id, kind = "company", name }, HttpStatusCode.Created);
        await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{id}/link", new { lexwareContactId = lexwareId });
        return (id, lexwareId);
    }

    private async Task<Guid> BauakteAsync(Guid customerId, object? project = null)
    {
        var id = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/bauakten",
            new { id, title = "Halle Huber", customerId, status = "planned", street = "Gewerbering 3", postalCode = "83026", city = "Rosenheim", project },
            HttpStatusCode.Created);
        return id;
    }

    private Task<SalesVoucherResponse> CreateAsync(string type, Guid? bauakteId = null, Guid? contactId = null) =>
        SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, "/api/v1/sales/vouchers", new { id = Guid.CreateVersion7(), type, bauakteId, contactId },
            HttpStatusCode.Created);

    private Task<SalesVoucherResponse> SaveAsync(SalesVoucherResponse v, object[] lines, string? title = null, string taxType = "net") =>
        SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Put, $"/api/v1/sales/vouchers/{v.Id}", new
        {
            version = v.Version, title = title ?? v.Title, introduction = v.Introduction, remark = v.Remark, validUntil = v.ValidUntil, serviceDate = v.ServiceDate,
            serviceEndDate = v.ServiceEndDate, taxType, paymentConditionId = v.PaymentConditionId, paymentLabel = v.PaymentLabel, paymentDuration = v.PaymentDuration,
            lines,
        });

    private static object Item(string name, decimal qty, decimal price, string unit = "m²", decimal tax = 19m) =>
        new { kind = "item", name, quantity = qty, unit, netPrice = price, taxRate = tax };

    // ---------- Schreibsperre ----------

    [Fact]
    public void WriteAllowed_SalesVouchers_OnlyDraftOrExplicitFinalize_AndPrecedingId()
    {
        foreach (var path in new[] { "/v1/quotations", "/v1/order-confirmations", "/v1/invoices", "/v1/credit-notes" })
        {
            LexwareClient.WriteAllowed(HttpMethod.Post, path).ShouldBeTrue();
            LexwareClient.WriteAllowed(HttpMethod.Post, path, "?precedingSalesVoucherId=1b2c3d4e-aaaa-bbbb-cccc-1234567890ab").ShouldBeTrue();
            LexwareClient.WriteAllowed(HttpMethod.Post, path, "?finalize=true").ShouldBeFalse(); // ohne Freigabe
            LexwareClient.WriteAllowed(HttpMethod.Post, path, "?finalize=true", finalize: true).ShouldBeTrue();
            LexwareClient.WriteAllowed(HttpMethod.Post, path, "?finalize=true&precedingSalesVoucherId=abc-1", finalize: true).ShouldBeTrue();
            LexwareClient.WriteAllowed(HttpMethod.Post, path, "?finalize=false", finalize: true).ShouldBeFalse();
            LexwareClient.WriteAllowed(HttpMethod.Post, path, "?precedingSalesVoucherId=a&precedingSalesVoucherId=b").ShouldBeFalse();
            LexwareClient.WriteAllowed(HttpMethod.Post, path, "?precedingSalesVoucherId=a%2Fb").ShouldBeFalse();
            LexwareClient.WriteAllowed(HttpMethod.Post, path, "?other=1").ShouldBeFalse();
            LexwareClient.WriteAllowed(HttpMethod.Put, path + "/abc").ShouldBeFalse(); // Belege werden nie geändert
            LexwareClient.WriteAllowed(HttpMethod.Delete, path + "/abc").ShouldBeFalse();
        }

        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/dunnings").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/delivery-notes").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/contacts", "?finalize=true", finalize: true).ShouldBeFalse();
    }

    [Fact]
    public void Rules_DeductionsSubtotalsAndPerTypeProblems()
    {
        SalesVoucherLine L(string kind, decimal? qty = null, decimal? price = null, int pos = 0) =>
            new() { Kind = kind, Name = "x", Quantity = qty, Unit = "m²", NetPrice = price, TaxRate = 19m, Position = pos };
        var invoice = new SalesVoucher
        {
            VoucherType = SalesTypes.Invoice, Title = "Schlussrechnung", Purpose = SalesTypes.PurposeFinal,
            Lines = [L("item", 100m, 25m, 1), L("subtotal", pos: 2), L("deduction", price: -1000m, pos: 3)],
        };
        SalesRules.Totals(invoice.Lines, "net").Net.ShouldBe(1500m);
        SalesRules.Subtotal(invoice.Lines.OrderBy(l => l.Position).ToList(), 1).ShouldBe(2500m);
        SalesRules.Problems(invoice).ShouldContain(p => p.Contains("Leistungsdatum", StringComparison.Ordinal));
        invoice.ServiceDate = new DateOnly(2026, 11, 2);
        invoice.ServiceEndDate = new DateOnly(2026, 11, 20);
        SalesRules.Problems(invoice).ShouldBeEmpty();

        var lexware = SalesRules.ToLexware(invoice, "c1", new DateOnly(2026, 11, 28), new Dictionary<Guid, CatalogItem>());
        lexware.Lines.Select(l => l.Type).ShouldBe(["custom", "text", "custom"]);
        lexware.Lines[1].Description.ShouldBe("Zwischensumme netto: 2.500,00 €");
        lexware.Lines[2].NetPrice.ShouldBe(-1000m);
        var body = LexwareClient.SalesBody(lexware);
        body["shippingConditions"]!["shippingType"]!.GetValue<string>().ShouldBe("serviceperiod");
        body["shippingConditions"]!["shippingEndDate"]!.GetValue<string>().ShouldStartWith("2026-11-20");
        body["expirationDate"].ShouldBeNull();

        var credit = new SalesVoucher { VoucherType = SalesTypes.CreditNote, Title = "Gutschrift", Lines = [L("item", 1m, 50m, 1), L("deduction", price: -10m, pos: 2)] };
        SalesRules.Problems(credit).ShouldContain("Position 2: Abzüge gibt es nur in Rechnungen.");
        var quotation = new SalesVoucher { VoucherType = SalesTypes.Quotation, Title = "Angebot", Lines = [L("item", 1m, 50m, 1)] };
        SalesRules.Problems(quotation).ShouldContain("Bitte geben Sie an, bis wann das Angebot gültig ist.");
    }

    // ---------- Belegarten, Festschreiben, Belegkette ----------

    [Fact]
    public async Task Invoice_FromQuotation_ChainServicePeriod_FinalizeOnlyWithModuleRightAndConfirmation()
    {
        await ConfigureAsync();
        var (customer, _) = await LinkedCustomerAsync();
        var bauakte = await BauakteAsync(customer);
        var quotation = await CreateAsync("quotation", bauakte);
        quotation.Title.ShouldBe("Angebot");
        quotation = await SaveAsync(quotation, [Item("Zementestrich CT-C25-F4", 200m, 22m), new { kind = "subtotal", name = "" }, new { kind = "text", name = "Hinweis" }]);
        quotation.Lines[1].LineNet.ShouldBe(4400m);
        var sentQuotation = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{quotation.Id}/send");
        sentQuotation.State.ShouldBe("sent");
        sentQuotation.Status.ShouldBe("draft");
        sentQuotation.Followers.ShouldBe(["orderconfirmation", "invoice"]);
        _lexware.CallsTo("POST", "/v1/quotations").Single().Query.ShouldBeEmpty();

        // In Rechnung umwandeln: Positionen übernommen, Belegkette über precedingSalesVoucherId.
        var invoice = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{quotation.Id}/copy",
            new { id = Guid.CreateVersion7(), type = "invoice" }, HttpStatusCode.Created);
        invoice.Type.ShouldBe("invoice");
        invoice.PrecedingId.ShouldBe(quotation.Id);
        invoice.Lines.Count.ShouldBe(3);
        invoice.ServiceDate.ShouldNotBeNull(); // ohne Einsätze: heute
        invoice.Chain.Select(c => c.Type).ShouldBe(["quotation", "invoice"]);
        invoice.Chain.Single(c => c.Current).Id.ShouldBe(invoice.Id);

        // Festschreiben: ohne Modul verweigert, ohne Bestätigung abgelehnt, mit beidem festgeschrieben.
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{invoice.Id}/send", HttpStatusCode.Forbidden, new { finalize = true, confirmed = true }))
            .ShouldStartWith("PERMISSION_DENIED");
        await ModulesAsync("salesFinalize");
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{invoice.Id}/send", HttpStatusCode.UnprocessableEntity, new { finalize = true }))
            .ShouldContain("bestätigen Sie das Festschreiben");
        _lexware.CallsTo("POST", "/v1/invoices").ShouldBeEmpty();
        var final = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{invoice.Id}/send", new { finalize = true, confirmed = true });
        final.Finalized.ShouldBeTrue();
        final.Status.ShouldBe("open");
        final.LexwareNumber!.ShouldStartWith("IN-");
        var call = _lexware.CallsTo("POST", "/v1/invoices").ShouldHaveSingleItem();
        call.Query.ShouldContain("finalize=true");
        call.Query.ShouldContain("precedingSalesVoucherId=" + sentQuotation.LexwareId);
        var body = JsonNode.Parse(call.Body!)!;
        body["shippingConditions"]!["shippingType"]!.GetValue<string>().ShouldBe("service");
        body["lineItems"]![1]!["type"]!.GetValue<string>().ShouldBe("text");

        // Nie ändern, nie löschen: außer Kontakten nur lesende Aufrufe und POST auf Belege.
        _lexware.Calls.Where(c => c.Method != "GET" && c.Method != "POST" && !c.Path.StartsWith("/v1/contacts", StringComparison.Ordinal)).ShouldBeEmpty();
        (await ProblemAsync(_admin, HttpMethod.Put, $"/api/v1/sales/vouchers/{invoice.Id}", HttpStatusCode.Conflict, new { version = final.Version, title = "x" }))
            .ShouldStartWith("LEXWARE_SALES_SENT");

        // Zahlstand und Status aus Lexware Office.
        _lexware.OpenAmounts[final.LexwareId!] = 1000m;
        var refreshed = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Get, $"/api/v1/sales/vouchers/{invoice.Id}?refresh=true");
        refreshed.OpenAmount.ShouldBe(1000m);
        refreshed.PaymentStatus.ShouldBe("openRevenue");
    }

    [Fact]
    public async Task OrderConfirmationAndCreditNote_AsDrafts_InvoiceNeedsServiceDate()
    {
        await ConfigureAsync();
        var (customer, _) = await LinkedCustomerAsync();
        var order = await CreateAsync("orderconfirmation", contactId: customer);
        order = await SaveAsync(order, [Item("Estrich", 10m, 30m)]);
        order.Problems.ShouldBeEmpty();
        await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{order.Id}/send");
        _lexware.CallsTo("POST", "/v1/order-confirmations").ShouldHaveSingleItem().Query.ShouldBeEmpty();

        var invoice = await CreateAsync("invoice", contactId: customer);
        invoice = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Put, $"/api/v1/sales/vouchers/{invoice.Id}", new
        {
            version = invoice.Version, title = "Rechnung", taxType = "net", lines = new[] { Item("Estrich", 10m, 30m) },
        });
        invoice.Problems.ShouldContain(p => p.Contains("Leistungsdatum", StringComparison.Ordinal));
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{invoice.Id}/send", HttpStatusCode.UnprocessableEntity)).ShouldContain("Leistungsdatum");

        var credit = await CreateAsync("creditnote", contactId: customer);
        credit.PaymentLabel.ShouldBeNull(); // Gutschriften ohne Zahlungsbedingung
        credit = await SaveAsync(credit, [Item("Gutschrift Nachlass", 1m, 150m, "pauschal")]);
        await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{credit.Id}/send");
        JsonNode.Parse(_lexware.CallsTo("POST", "/v1/credit-notes").Single().Body!)!["paymentConditions"].ShouldBeNull();

        // Übersicht aus der Belegliste: alle Belegarten, mit offenem Betrag nur bei zahlbaren Belegen.
        var list = await SendAsync<SalesListResponse>(_admin, HttpMethod.Get, "/api/v1/sales/vouchers");
        list.LexwareError.ShouldBeNull();
        list.Items.Select(i => i.Type).ShouldBe(["orderconfirmation", "creditnote", "invoice"], ignoreOrder: true);
        list.Items.Single(i => i.Type == "invoice").Status.ShouldBe("local"); // noch nicht gesendet
        list.Items.Single(i => i.Type == "orderconfirmation").OpenAmount.ShouldBeNull();
        (await SendAsync<SalesListResponse>(_admin, HttpMethod.Get, "/api/v1/sales/vouchers?type=creditnote")).Items.ShouldHaveSingleItem();
    }

    // ---------- Rechnungsplan, Abschlag, Schlussrechnung ----------

    [Fact]
    public async Task BillingPlan_DownPaymentsFromProgress_FinalInvoiceDeductsThem()
    {
        await ConfigureAsync("salesFinalize");
        var (customer, _) = await LinkedCustomerAsync();
        var bauakte = await BauakteAsync(customer);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            // Räume: 100 m² eingebracht von 250 m² (40 Prozent Leistungsstand).
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO rooms (id, bauakte_id, name, area_square_meters, status, underfloor_heating, version, created_at, updated_at)
                VALUES ({Guid.CreateVersion7()}, {bauakte}, 'Halle', 100, 'Laid', false, 1, now(), now()),
                       ({Guid.CreateVersion7()}, {bauakte}, 'Lager', 150, 'Planned', false, 1, now(), now())
                """, Ct);
        }

        var empty = await SendAsync<SalesPlanResponse>(_admin, HttpMethod.Get, $"/api/v1/sales/bauakten/{bauakte}/plan");
        empty.OrderNet.ShouldBeNull();
        empty.Hints.ShouldContain(h => h.Contains("Auftragssumme", StringComparison.Ordinal));
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/sales/bauakten/{bauakte}/down-payment", HttpStatusCode.UnprocessableEntity,
            new { id = Guid.CreateVersion7() })).ShouldContain("Auftragssumme");

        // Auftrag: Auftragsbestätigung über 10.000 € netto, festgeschrieben.
        var order = await CreateAsync("orderconfirmation", bauakte);
        order = await SaveAsync(order, [Item("Estrich liefern und einbauen", 250m, 40m)]);
        await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{order.Id}/send", new { finalize = true, confirmed = true });
        var plan = await SendAsync<SalesPlanResponse>(_admin, HttpMethod.Get, $"/api/v1/sales/bauakten/{bauakte}/plan");
        plan.OrderNet.ShouldBe(10000m);
        plan.OrderSource!.ShouldStartWith("Auftragsbestätigung OR-");
        plan.ProgressPercent.ShouldBe(40m);
        plan.SuggestedPercent.ShouldBe(40m);
        plan.NotBilledNet.ShouldBe(10000m);

        // Abschlag nach Leistungsstand: 40 Prozent, festgeschrieben.
        var down = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/bauakten/{bauakte}/down-payment",
            new { id = Guid.CreateVersion7() }, HttpStatusCode.Created);
        down.Purpose.ShouldBe("downPayment");
        down.TypeName.ShouldBe("Abschlagsrechnung");
        down.Title.ShouldBe("1. Abschlagsrechnung");
        down.Totals.Net.ShouldBe(4000m);
        down.Lines.ShouldHaveSingleItem().Name.ShouldBe("1. Abschlag: 40 % der Auftragssumme");
        down.PrecedingId.ShouldBe(order.Id);
        down = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{down.Id}/send", new { finalize = true, confirmed = true });
        _lexware.CallsTo("POST", "/v1/invoices").Single().Query.ShouldContain("precedingSalesVoucherId=");

        plan = await SendAsync<SalesPlanResponse>(_admin, HttpMethod.Get, $"/api/v1/sales/bauakten/{bauakte}/plan");
        plan.DownPaymentCount.ShouldBe(1);
        plan.DownPaymentsNet.ShouldBe(4000m);
        plan.NotBilledNet.ShouldBe(6000m);
        plan.OpenGross.ShouldBe(4760m);
        plan.SuggestedPercent.ShouldBe(0m);
        plan.Text.ShouldContain("1 Abschlag 4.000,00 €");

        // Zweiter Abschlag mit festem Prozentsatz.
        var second = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/bauakten/{bauakte}/down-payment",
            new { id = Guid.CreateVersion7(), percent = 25m }, HttpStatusCode.Created);
        second.Title.ShouldBe("2. Abschlagsrechnung");
        second.Totals.Net.ShouldBe(2500m);
        await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{second.Id}/send", new { finalize = true, confirmed = true });

        // Schlussrechnung: alle Positionen des Auftrags, Abschläge als Abzugszeilen mit Nummer und Datum.
        var final = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/bauakten/{bauakte}/final-invoice",
            new { id = Guid.CreateVersion7() }, HttpStatusCode.Created);
        final.Purpose.ShouldBe("final");
        final.TypeName.ShouldBe("Schlussrechnung");
        final.Lines.Select(l => l.Kind).ShouldBe(["item", "subtotal", "deduction", "deduction"]);
        final.Lines[2].Name.ShouldStartWith("Abzüglich 1. Abschlagsrechnung Nr. IN-");
        final.Lines[2].Name.ShouldContain(" vom ");
        final.Lines[2].NetPrice.ShouldBe(-4000m);
        final.Totals.Net.ShouldBe(3500m);
        final.Problems.ShouldBeEmpty();
        await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{final.Id}/send");
        var body = JsonNode.Parse(_lexware.CallsTo("POST", "/v1/invoices").Last().Body!)!;
        body["lineItems"]![2]!["unitPrice"]!["netAmount"]!.GetValue<decimal>().ShouldBe(-4000m);
        body["lineItems"]![1]!["type"]!.GetValue<string>().ShouldBe("text");

        plan = await SendAsync<SalesPlanResponse>(_admin, HttpMethod.Get, $"/api/v1/sales/bauakten/{bauakte}/plan");
        plan.FinalInvoiceExists.ShouldBeTrue();
        plan.NotBilledNet.ShouldBe(0m);
        plan.Vouchers.Count.ShouldBe(4);
    }

    // ---------- Rechte und Module ----------

    [Fact]
    public async Task Rights_PerVoucherType_OfficeWithoutFinalize_WorkerDenied_SubmoduleOff()
    {
        await ConfigureAsync("salesFinalize");
        var (customer, _) = await LinkedCustomerAsync();
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using (var denied = await worker.GetAsync(new Uri("/api/v1/sales/vouchers", UriKind.Relative), Ct))
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var options = await SendAsync<SalesOptionsResponse>(office, HttpMethod.Get, "/api/v1/sales/options?type=invoice");
        options.Types.Single(t => t.Type == "invoice").CanEdit.ShouldBeTrue();
        options.Types.Single(t => t.Type == "invoice").CanFinalize.ShouldBeFalse(); // Festschreiben ist ein eigenes Recht
        var invoice = await SendAsync<SalesVoucherResponse>(office, HttpMethod.Post, "/api/v1/sales/vouchers",
            new { id = Guid.CreateVersion7(), type = "invoice", contactId = customer }, HttpStatusCode.Created);
        invoice.CanFinalize.ShouldBeFalse();
        (await ProblemAsync(office, HttpMethod.Post, $"/api/v1/sales/vouchers/{invoice.Id}/send", HttpStatusCode.Forbidden, new { finalize = true, confirmed = true }))
            .ShouldStartWith("PERMISSION_DENIED");

        // Untermodul aus: Belegart weder sichtbar noch anlegbar.
        await SendAsync<JsonObject>(_admin, HttpMethod.Put, "/api/v1/settings/modules", new { modules = new { salesCreditNotes = false } });
        (await ProblemAsync(_admin, HttpMethod.Post, "/api/v1/sales/vouchers", HttpStatusCode.Forbidden, new { id = Guid.CreateVersion7(), type = "creditnote",
            contactId = customer })).ShouldStartWith("PERMISSION_DENIED");

        // Verkauf aus: alles aus.
        await SendAsync<JsonObject>(_admin, HttpMethod.Put, "/api/v1/settings/modules", new
        {
            modules = new
            {
                sales = false, quotations = false, salesOrderConfirmations = false, salesInvoices = false, salesCreditNotes = false, salesFinalize = false,
                salesPayments = false,
            },
        });
        (await ProblemAsync(_admin, HttpMethod.Post, "/api/v1/sales/vouchers", HttpStatusCode.Conflict, new { id = Guid.CreateVersion7(), type = "invoice",
            contactId = customer })).ShouldStartWith("MODULE_DISABLED");
        (await ProblemAsync(office, HttpMethod.Get, "/api/v1/sales/vouchers", HttpStatusCode.Conflict)).ShouldStartWith("MODULE_DISABLED");
        var modules = await SendAsync<JsonObject>(_admin, HttpMethod.Get, "/api/v1/settings/modules");
        modules.ToJsonString().ShouldContain("\"quotations\"");
    }
}
