using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Lexware;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Lexware;

/// <summary>
/// Artikelabgleich („Lexware Office: Artikel“), Angebote (Beta) und der automatische Status der Bauakte gegen die
/// nachgebildete API. Geprüft wird insbesondere, dass nur erlaubte Aufrufe schreiben (Artikel nur bei passender Richtung,
/// Angebote nur als Entwurf ohne <c>finalize</c>), dass nichts doppelt angelegt wird und dass der Status nur vorwärts geht.
/// </summary>
public sealed class LexwareArticleQuotationTests(PostgresFixture postgres) : IAsyncLifetime
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

    private async Task ConfigureAsync(object? settings = null, params string[] modules)
    {
        await ModulesAsync(["lexware", .. modules]);
        await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { apiKey = FakeLexware.Key });
        if (settings is not null)
        {
            await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", settings);
        }
    }

    private async Task<T> DbAsync<T>(Func<EstrichOneDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>());
    }

    private async Task<(Guid Customer, string LexwareId)> LinkedCustomerAsync(string name = "Huber Bau GmbH")
    {
        var lexwareId = _lexware.AddContact(name, email: $"{Guid.NewGuid():N}@huber.example");
        var id = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/contacts", new { lexwareCreateAnyway = true, id, kind = "company", name }, HttpStatusCode.Created);
        await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{id}/link", new { lexwareContactId = lexwareId });
        return (id, lexwareId);
    }

    private async Task<Guid> BauakteAsync(Guid? customerId, string status = "planned", object? project = null)
    {
        var id = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/bauakten",
            new { id, title = "EFH Huber", customerId, status, street = "Am Anger 7", postalCode = "83022", city = "Rosenheim", project },
            HttpStatusCode.Created);
        return id;
    }

    // ---------- Artikel ----------

    [Fact]
    public void ArticleMerge_PullsLexwareChanges_KeepsLocalChangesAsDeviations_UnlessBoth()
    {
        var item = new CatalogItem { Title = "Zementestrich CT-C25-F4", Unit = "m²", NetPrice = 24m, TaxRate = 19m, Kind = CatalogItemKind.Service };
        var baseline = LexwareArticleRules.Local(item);
        var remote = new Dictionary<string, string?>(baseline) { [LexwareArticleFields.NetPrice] = "26.5" };
        var pulled = LexwareArticleRules.Merge(baseline, LexwareArticleRules.Local(item), remote, LexwareArticleSync.FromLexware);
        pulled.ToEstrichOne.ShouldBe(new Dictionary<string, string?> { [LexwareArticleFields.NetPrice] = "26.5" });
        pulled.Deviations.ShouldBeEmpty();

        item.Unit = "qm";
        var local = LexwareArticleRules.Local(item);
        var fromOnly = LexwareArticleRules.Merge(baseline, local, baseline, LexwareArticleSync.FromLexware);
        fromOnly.ToLexware.ShouldBeEmpty();
        fromOnly.Deviations.ShouldHaveSingleItem().Field.ShouldBe(LexwareArticleFields.Unit);
        LexwareArticleRules.Merge(baseline, local, baseline, LexwareArticleSync.Both).ToLexware.Keys.ShouldBe([LexwareArticleFields.Unit]);
        LexwareArticleRules.Merge(null, local, baseline, LexwareArticleSync.Both).ToLexware.ShouldBeEmpty(); // Erstabgleich überschreibt nichts

        LexwareArticleRules.Reasons(item, new LexwareArticle("x", "Zementestrich  CT-C25-F4!", null, "SERVICE", null, null, null, 1, 1, "NET", 19, false, 1, null))
            .ShouldBe(["gleiche Bezeichnung"]);
    }

    [Fact]
    public async Task ArticleSync_ImportsFromLexware_DuplicatesWaitForDecision_WritesNothingByDefault()
    {
        await ConfigureAsync(null, "lexwareArticles");
        var estrich = _lexware.AddArticle("Zementestrich CT-C25-F4 einbauen", number: "L-100", net: 24m);
        _lexware.AddArticle("Randdämmstreifen 8 mm", type: "PRODUCT", number: "L-200", net: 0.9m, unit: "lfm");
        _lexware.AddArticle("Alter Artikel", archived: true);
        var dup = _lexware.AddArticle("Anfahrt", number: "A-1", unit: "pauschal");
        var local = Guid.CreateVersion7();
        await SendAsync<CatalogItemResponse>(_admin, HttpMethod.Put, $"/api/v1/lexware/articles/{local}",
            new { id = local, title = "Anfahrt", kind = "service", unit = "pauschal", netPrice = 80m, taxRate = 19m });

        var run = await SendAsync<LexwareArticleSyncResponse>(_admin, HttpMethod.Post, "/api/v1/lexware/articles/sync");
        run.Imported.ShouldBe(2);
        run.Duplicates.ShouldBe(1);
        var list = await SendAsync<CatalogListResponse>(_admin, HttpMethod.Get, "/api/v1/lexware/articles");
        list.Direction.ShouldBe(LexwareArticleSync.FromLexware);
        list.Items.Count.ShouldBe(3);
        list.Items.Single(i => i.LexwareId == estrich).Sync.State.ShouldBe("synced");
        list.Items.Single(i => i.Id == local).Sync.State.ShouldBe("local");

        var reconciliation = await SendAsync<LexwareArticleReconciliationResponse>(_admin, HttpMethod.Get, "/api/v1/lexware/articles/reconciliation");
        reconciliation.OnlyLexware.ShouldHaveSingleItem().Article.Id.ShouldBe(dup);
        reconciliation.OnlyLexware[0].Candidates.ShouldHaveSingleItem().Reasons.ShouldBe(["gleiche Bezeichnung"]);
        (await ProblemAsync(_admin, HttpMethod.Post, "/api/v1/lexware/articles/import", HttpStatusCode.Conflict, new { lexwareId = dup }))
            .ShouldStartWith("LEXWARE_ARTICLE_DUPLICATE");
        var linked = await SendAsync<CatalogItemResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/articles/{local}/link", new { lexwareId = dup });
        linked.Sync.State.ShouldBe("deviations"); // Preis 80 gegen 10: Erstabgleich überschreibt nichts

        // Änderung in Lexware Office wird übernommen; eigene Änderung bleibt bei „nur aus Lexware“ als Abweichung.
        _lexware.EditArticle(estrich, a => a["price"]!["netPrice"] = 26.5m);
        var item = list.Items.Single(i => i.LexwareId == estrich);
        await SendAsync<CatalogItemResponse>(_admin, HttpMethod.Put, $"/api/v1/lexware/articles/{item.Id}",
            new { id = item.Id, title = item.Title, kind = "service", articleNumber = item.ArticleNumber, unit = "qm", netPrice = item.NetPrice, taxRate = 19m, version = item.Version });
        await SendAsync<LexwareArticleSyncResponse>(_admin, HttpMethod.Post, "/api/v1/lexware/articles/sync");
        var after = (await SendAsync<CatalogListResponse>(_admin, HttpMethod.Get, "/api/v1/lexware/articles")).Items.Single(i => i.LexwareId == estrich);
        after.NetPrice.ShouldBe(26.5m);
        after.Unit.ShouldBe("qm");
        after.Sync.Deviations.ShouldHaveSingleItem().Field.ShouldBe("unitName");
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/lexware/articles/{item.Id}/resolve", HttpStatusCode.Conflict, new { source = "estrichOne" }))
            .ShouldStartWith("LEXWARE_ARTICLE_SYNC_DIRECTION");
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/lexware/articles/{local}/create", HttpStatusCode.Conflict, new { }))
            .ShouldStartWith("LEXWARE_ARTICLE_SYNC_DIRECTION");

        // Vorgabe „aus Lexware Office“: In Lexware Office wurde nichts geschrieben.
        _lexware.Calls.ShouldNotContain(c => c.Method != "GET");
    }

    [Fact]
    public async Task ArticleSync_BothDirections_CreatesAndUpdatesWithVersion_NeverDeletes()
    {
        await ConfigureAsync(new { articleSync = "both" }, "lexwareArticles");
        var remote = _lexware.AddArticle("Fließestrich CAF einbauen", number: "L-300", net: 19m);
        var local = Guid.CreateVersion7();
        await SendAsync<CatalogItemResponse>(_admin, HttpMethod.Put, $"/api/v1/lexware/articles/{local}",
            new { id = local, title = "Schnellestrich Zuschlag", kind = "service", articleNumber = "E-1", unit = "m²", netPrice = 4.5m, taxRate = 19m });
        var run = await SendAsync<LexwareArticleSyncResponse>(_admin, HttpMethod.Post, "/api/v1/lexware/articles/sync");
        run.Imported.ShouldBe(1);
        run.Created.ShouldBe(1);
        var post = JsonNode.Parse(_lexware.CallsTo("POST", "/v1/articles").ShouldHaveSingleItem().Body!)!;
        post["title"]!.GetValue<string>().ShouldBe("Schnellestrich Zuschlag");
        post["price"]!["netPrice"]!.GetValue<decimal>().ShouldBe(4.5m);
        post["price"]!["leadingPrice"]!.GetValue<string>().ShouldBe("NET");

        // Eigene Änderung geht mit Version nach Lexware Office; dortige Felder (Notiz) bleiben erhalten.
        var item = (await SendAsync<CatalogListResponse>(_admin, HttpMethod.Get, "/api/v1/lexware/articles")).Items.Single(i => i.LexwareId == remote);
        await SendAsync<CatalogItemResponse>(_admin, HttpMethod.Put, $"/api/v1/lexware/articles/{item.Id}",
            new { id = item.Id, title = item.Title, kind = "service", articleNumber = item.ArticleNumber, unit = "m²", netPrice = 21m, taxRate = 19m, version = item.Version });
        await SendAsync<LexwareArticleSyncResponse>(_admin, HttpMethod.Post, "/api/v1/lexware/articles/sync");
        var put = JsonNode.Parse(_lexware.CallsTo("PUT", "/v1/articles/").ShouldHaveSingleItem().Body!)!;
        put["version"]!.GetValue<int>().ShouldBe(1);
        put["note"]!.GetValue<string>().ShouldBe("intern");
        _lexware.Articles[remote]["price"]!["netPrice"]!.GetValue<decimal>().ShouldBe(21m);

        // In Lexware Office gelöscht: bleibt in EstrichONE, gekennzeichnet; nie ein DELETE.
        _lexware.Articles.TryRemove(remote, out _);
        await SendAsync<LexwareArticleSyncResponse>(_admin, HttpMethod.Post, "/api/v1/lexware/articles/sync");
        (await SendAsync<CatalogListResponse>(_admin, HttpMethod.Get, "/api/v1/lexware/articles")).Items.Single(i => i.Id == item.Id).Sync.State.ShouldBe("missing");
        _lexware.Calls.ShouldNotContain(c => c.Method == "DELETE");
    }

    [Fact]
    public async Task ArticleEndpoints_NeedModuleAndRights()
    {
        await ConfigureAsync();
        // Administratoren dürfen bei ausgeschaltetem Modul lesen; schreiben nicht.
        (await ProblemAsync(_admin, HttpMethod.Post, "/api/v1/lexware/articles/sync", HttpStatusCode.Conflict)).ShouldStartWith("MODULE_DISABLED");
        await ModulesAsync("sales", "quotations");
        (await SendAsync<CatalogListResponse>(_admin, HttpMethod.Get, "/api/v1/lexware/articles")).SyncEnabled.ShouldBeFalse();
        (await ProblemAsync(_admin, HttpMethod.Post, "/api/v1/lexware/articles/sync", HttpStatusCode.Conflict)).ShouldStartWith("MODULE_DISABLED");
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var response = await worker.GetAsync(new Uri("/api/v1/lexware/articles", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------- Angebote ----------

    [Fact]
    public void QuotationTotals_PerTaxRate_And13bWithoutTax()
    {
        SalesVoucherLine Line(decimal qty, decimal price, decimal tax, decimal? discount = null) =>
            new() { Kind = SalesVoucherLine.KindItem, Name = "x", Quantity = qty, Unit = "m²", NetPrice = price, TaxRate = tax, DiscountPercent = discount };
        List<SalesVoucherLine> lines = [Line(120.5m, 24m, 19m), Line(10m, 3.333m, 7m, 10m), new() { Kind = SalesVoucherLine.KindText, Name = "Hinweis" }];
        var totals = SalesRules.Totals(lines, SalesVoucher.TaxNet);
        totals.Net.ShouldBe(2892m + 30m);
        totals.Tax.ShouldBe(549.48m + 2.1m);
        totals.Gross.ShouldBe(totals.Net + totals.Tax);
        SalesRules.Totals(lines, SalesVoucher.TaxConstruction13b).Tax.ShouldBe(0m);
    }

    [Fact]
    public async Task Quotation_NeedsLinkedCustomer_SuggestsArea_SendsDraftOnce_ThenReadOnly()
    {
        await ConfigureAsync(null, "sales", "quotations");
        var unlinked = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/contacts", new { lexwareCreateAnyway = true, id = unlinked, kind = "company", name = "Neu GmbH" },
            HttpStatusCode.Created);
        var first = await BauakteAsync(unlinked);
        (await ProblemAsync(_admin, HttpMethod.Post, "/api/v1/sales/vouchers", HttpStatusCode.Conflict, new { id = Guid.CreateVersion7(), type = "quotation", bauakteId = first }))
            .ShouldContain("LEXWARE_CUSTOMER_NOT_LINKED");

        var (customer, lexwareContact) = await LinkedCustomerAsync();
        var bauakte = await BauakteAsync(customer, project: new { totalArea = 142.5m, nominalThicknessMm = 50 });
        var suggestions = await SendAsync<SalesSuggestionsResponse>(_admin, HttpMethod.Get, $"/api/v1/sales/suggestions?bauakteId={bauakte}");
        suggestions.Lines.ShouldHaveSingleItem().Quantity.ShouldBe(142.5m);
        suggestions.Lines[0].Unit.ShouldBe("m²");
        suggestions.Hints.ShouldContain(h => h.Contains("Randdämmstreifen", StringComparison.Ordinal));

        var options = await SendAsync<SalesOptionsResponse>(_admin, HttpMethod.Get, "/api/v1/sales/options?type=quotation");
        options.PaymentConditions.Count.ShouldBe(2);
        options.PaymentConditionsError.ShouldBeNull();

        var id = Guid.CreateVersion7();
        var draft = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, "/api/v1/sales/vouchers", new { id, type = "quotation", bauakteId = bauakte }, HttpStatusCode.Created);
        draft.CustomerLinked.ShouldBeTrue();
        draft.Introduction!.ShouldContain("EFH Huber");
        draft.State.ShouldBe("draft");
        var condition = options.PaymentConditions[1];
        draft = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Put, $"/api/v1/sales/vouchers/{id}", new
        {
            version = draft.Version, title = "Angebot Estricharbeiten", introduction = draft.Introduction, validUntil = draft.ValidUntil, taxType = "net",
            paymentConditionId = condition.Id, paymentLabel = condition.Label, paymentDuration = condition.Duration, paymentDiscountPercent = condition.DiscountPercent,
            paymentDiscountRange = condition.DiscountRange,
            lines = new object[]
            {
                new { kind = "item", name = "Zementestrich CT-C25-F4", quantity = 142.5m, unit = "m²", netPrice = 24m, taxRate = 19m },
                new { kind = "item", name = "Randdämmstreifen", quantity = 80m, unit = "lfm", netPrice = 1.2m, taxRate = 19m, discountPercent = 10m },
                new { kind = "text", name = "Hinweis", description = "Bauseits Strom und Wasser." },
            },
        });
        draft.Totals.Net.ShouldBe(3420m + 86.4m);
        draft.Problems.ShouldBeEmpty();

        var sent = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{id}/send");
        sent.State.ShouldBe("sent");
        sent.Editable.ShouldBeFalse();
        sent.LexwareUrl!.ShouldContain("/permalink/quotations/edit/");
        var post = _lexware.CallsTo("POST", "/v1/quotations").ShouldHaveSingleItem();
        post.Query.ShouldBeEmpty(); // kein finalize: Entwurf
        var body = JsonNode.Parse(post.Body!)!;
        body["address"]!["contactId"]!.GetValue<string>().ShouldBe(lexwareContact);
        body["taxConditions"]!["taxType"]!.GetValue<string>().ShouldBe("net");
        body["lineItems"]!.AsArray().Count.ShouldBe(3);
        body["lineItems"]![1]!["discountPercentage"]!.GetValue<decimal>().ShouldBe(10m);
        body["lineItems"]![2]!["type"]!.GetValue<string>().ShouldBe("text");
        body["paymentConditions"]!["paymentTermDuration"]!.GetValue<int>().ShouldBe(30);

        // Zweimal senden legt kein zweites Angebot an; Bearbeiten ist gesperrt.
        await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{id}/send");
        _lexware.CallsTo("POST", "/v1/quotations").Count().ShouldBe(1);
        (await ProblemAsync(_admin, HttpMethod.Put, $"/api/v1/sales/vouchers/{id}", HttpStatusCode.Conflict, new { version = sent.Version, title = "x" }))
            .ShouldStartWith("LEXWARE_SALES_SENT");
        (await ProblemAsync(_admin, HttpMethod.Delete, $"/api/v1/sales/vouchers/{id}", HttpStatusCode.Conflict)).ShouldStartWith("LEXWARE_SALES_SENT");

        // Der Beleg ist der Bauakte zugeordnet und erscheint in der Belegliste.
        var vouchers = await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware");
        var voucher = vouchers.Vouchers.ShouldHaveSingleItem();
        voucher.Type.ShouldBe("quotation");
        voucher.LexwareId.ShouldBe(sent.LexwareId);
        voucher.Status.ShouldBe("draft");
        _lexware.Calls.ShouldNotContain(c => c.Method != "GET" && !(c.Method == "POST" && c.Path == "/v1/quotations"));
    }

    [Fact]
    public async Task Quotation_LostAnswer_SecondSendAdoptsExisting_InsteadOfSendingAgain()
    {
        await ConfigureAsync(null, "sales", "quotations");
        var (customer, _) = await LinkedCustomerAsync();
        var id = Guid.CreateVersion7();
        var draft = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, "/api/v1/sales/vouchers", new { id, type = "quotation", contactId = customer }, HttpStatusCode.Created);
        draft.BauakteId.ShouldBeNull();
        await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Put, $"/api/v1/sales/vouchers/{id}", new
        {
            version = draft.Version, title = "Angebot Garage", validUntil = draft.ValidUntil, taxType = "constructionService13b",
            lines = new object[] { new { kind = "item", name = "Estrich", quantity = 30m, unit = "m²", netPrice = 25m, taxRate = 19m } },
        });

        _lexware.QuotationLostAnswer = 504;
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{id}/send", HttpStatusCode.ServiceUnavailable)).ShouldStartWith("LEXWARE_UNAVAILABLE");
        var failed = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Get, $"/api/v1/sales/vouchers/{id}");
        failed.State.ShouldBe("failed");
        (await ProblemAsync(_admin, HttpMethod.Delete, $"/api/v1/sales/vouchers/{id}", HttpStatusCode.Conflict)).ShouldStartWith("LEXWARE_SALES_SENT");

        var sent = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{id}/send");
        sent.State.ShouldBe("sent");
        _lexware.CallsTo("POST", "/v1/quotations").Count().ShouldBe(1);
        _lexware.Vouchers.Values.Count(v => v["voucherType"]!.GetValue<string>() == "quotation").ShouldBe(1);
        var body = JsonNode.Parse(_lexware.CallsTo("POST", "/v1/quotations").Single().Body!)!;
        body["taxConditions"]!["taxType"]!.GetValue<string>().ShouldBe("constructionService13b");
        body["lineItems"]![0]!["unitPrice"]!["taxRatePercentage"]!.GetValue<decimal>().ShouldBe(0m);
    }

    [Fact]
    public async Task Quotation_TaxType13bRejected_TranslatedWithFix_RememberedUntilRecheck()
    {
        await ConfigureAsync(null, "sales", "quotations");
        _lexware.RejectedTaxTypes.Add("constructionService13b");
        var (customer, _) = await LinkedCustomerAsync();
        var id = Guid.CreateVersion7();
        var draft = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, "/api/v1/sales/vouchers", new { id, type = "quotation", contactId = customer }, HttpStatusCode.Created);
        object Body(long version, string taxType) => new
        {
            version, title = "Angebot Halle", validUntil = draft.ValidUntil, taxType,
            lines = new object[] { new { kind = "item", name = "Estrich", quantity = 100m, unit = "m²", netPrice = 22m, taxRate = 19m } },
        };
        draft = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Put, $"/api/v1/sales/vouchers/{id}", Body(draft.Version, "constructionService13b"));

        // Senden: Lexware Office lehnt ab, wie beim Kunden mit echtem Konto. Meldung auf Deutsch, Behebung „Netto“, Original nur technisch.
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sales/vouchers/{id}/send"))
        using (var response = await _admin.SendAsync(request, Ct))
        {
            var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
            response.StatusCode.ShouldBe(HttpStatusCode.FailedDependency);
            problem["code"]!.GetValue<string>().ShouldBe("LEXWARE_TAX_TYPE_NOT_ALLOWED");
            problem["detail"]!.GetValue<string>().ShouldBe(
                "Lexware Office lässt die Steuerart Bauleistung nach § 13b UStG für Ihr Unternehmen nicht zu. Prüfen Sie die Steuereinstellungen in Lexware Office oder senden Sie das Angebot mit der Steuerart Netto.");
            problem["fix"]!.GetValue<string>().ShouldBe("taxTypeNet");
            problem["technical"]!.GetValue<string>().ShouldBe("Tax type 'constructionService13b' not allowed for this organization");
            problem["issues"]![0]!["field"]!.GetValue<string>().ShouldBe("taxType");
        }

        var failed = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Get, $"/api/v1/sales/vouchers/{id}");
        failed.State.ShouldBe("failed");
        failed.SendError!.ShouldContain("§ 13b UStG");
        failed.SendError!.ShouldNotContain("not allowed");
        failed.SendErrorFix.ShouldBe("taxTypeNet");
        failed.SendErrorTechnical.ShouldBe("Tax type 'constructionService13b' not allowed for this organization");
        failed.Problems.ShouldContain(p => p.Contains("§ 13b UStG", StringComparison.Ordinal)); // gemerkt: so nicht erneut sendebereit

        // Die Option ist für Angebote gesperrt, mit Hinweis; andere Belegarten bleiben unberührt.
        var options = await SendAsync<SalesOptionsResponse>(_admin, HttpMethod.Get, "/api/v1/sales/options?type=quotation");
        var option = options.TaxTypes!.Single(t => t.Key == "constructionService13b");
        option.Allowed.ShouldBeFalse();
        option.Hint!.ShouldContain("erneut prüfen");
        options.TaxTypes!.Single(t => t.Key == "net").Allowed.ShouldBeTrue();
        var settings = await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware");
        settings.TaxTypes!.Rejections.ShouldHaveSingleItem().VoucherType.ShouldBe("quotation");

        // „Steuerart Netto übernehmen und erneut senden“: speichern mit net, senden. Nur ein Angebot entsteht.
        draft = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Put, $"/api/v1/sales/vouchers/{id}", Body(failed.Version, "net"));
        draft.State.ShouldBe("draft");
        draft.SendErrorFix.ShouldBeNull();
        var sent = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{id}/send");
        sent.State.ShouldBe("sent");
        _lexware.Vouchers.Values.Count(v => v["voucherType"]!.GetValue<string>() == "quotation").ShouldBe(1);
        JsonNode.Parse(_lexware.CallsTo("POST", "/v1/quotations").Last().Body!)!["taxConditions"]!["taxType"]!.GetValue<string>().ShouldBe("net");

        // Erneut prüfen: Ablehnungen werden verworfen, das Profil wird gelesen.
        _lexware.RejectedTaxTypes.Clear();
        var check = await SendAsync<LexwareTaxTypesResponse>(_admin, HttpMethod.Post, "/api/v1/settings/integrations/lexware/tax-types/check");
        check.Rejections.ShouldBeEmpty();
        check.CheckedAt.ShouldNotBeNull();
        (await SendAsync<SalesOptionsResponse>(_admin, HttpMethod.Get, "/api/v1/sales/options?type=quotation")).TaxTypes!.ShouldAllBe(t => t.Allowed);
    }

    [Fact]
    public async Task Quotation_SmallBusiness_OnlyVatfree_DefaultAndProblems()
    {
        _lexware.SmallBusiness = true;
        _lexware.ProfileTaxType = "vatfree";
        await ConfigureAsync(null, "sales", "quotations");
        var (customer, _) = await LinkedCustomerAsync();
        var options = await SendAsync<SalesOptionsResponse>(_admin, HttpMethod.Get, "/api/v1/sales/options?type=quotation");
        options.SmallBusiness.ShouldBeTrue();
        options.TaxTypes!.Where(t => t.Allowed).Select(t => t.Key).ShouldBe(["vatfree"]);
        var id = Guid.CreateVersion7();
        var draft = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, "/api/v1/sales/vouchers", new { id, type = "quotation", contactId = customer }, HttpStatusCode.Created);
        draft.TaxType.ShouldBe("vatfree");
        draft = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Put, $"/api/v1/sales/vouchers/{id}", new
        {
            version = draft.Version, title = "Angebot", validUntil = draft.ValidUntil, taxType = "net",
            lines = new object[] { new { kind = "item", name = "Estrich", quantity = 10m, unit = "m²", netPrice = 20m, taxRate = 19m } },
        });
        draft.Problems.ShouldContain(p => p.Contains("Kleinunternehmer", StringComparison.Ordinal));
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{id}/send", HttpStatusCode.UnprocessableEntity)).ShouldContain("Kleinunternehmer");
        draft = await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Put, $"/api/v1/sales/vouchers/{id}", new
        {
            version = draft.Version, title = "Angebot", validUntil = draft.ValidUntil, taxType = "vatfree",
            lines = new object[] { new { kind = "item", name = "Estrich", quantity = 10m, unit = "m²", netPrice = 20m, taxRate = 19m } },
        });
        draft.Totals.Tax.ShouldBe(0m);
        await SendAsync<SalesVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/sales/vouchers/{id}/send");
        var body = JsonNode.Parse(_lexware.CallsTo("POST", "/v1/quotations").Single().Body!)!;
        body["taxConditions"]!["taxType"]!.GetValue<string>().ShouldBe("vatfree");
        body["lineItems"]![0]!["unitPrice"]!["taxRatePercentage"]!.GetValue<decimal>().ShouldBe(0m);
    }

    [Fact]
    public async Task Quotations_NeedModuleAndRight()
    {
        await ConfigureAsync();
        (await ProblemAsync(_admin, HttpMethod.Post, "/api/v1/sales/vouchers", HttpStatusCode.Conflict, new { id = Guid.CreateVersion7(), type = "quotation" }))
            .ShouldStartWith("MODULE_DISABLED");
        await ModulesAsync("sales", "quotations");
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var denied = await worker.GetAsync(new Uri("/api/v1/sales/options?type=quotation", UriKind.Relative), Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var allowed = await office.GetAsync(new Uri("/api/v1/sales/options?type=quotation", UriKind.Relative), Ct);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- Status der Bauakte ----------

    [Fact]
    public void StatusRules_PaidNeedsAllInvoices_AndNoOpenCreditNote()
    {
        LexwareVoucherLink L(string type, string status, string id) => new() { VoucherType = type, Status = status, LexwareId = id };
        LexwareStatusRules.InvoicesPaid([]).Paid.ShouldBeFalse();
        LexwareStatusRules.InvoicesPaid([L("quotation", "accepted", "q")]).Paid.ShouldBeFalse();
        LexwareStatusRules.InvoicesPaid([L("invoice", "paid", "a"), L("invoice", "open", "b")]).Paid.ShouldBeFalse();
        LexwareStatusRules.InvoicesPaid([L("invoice", "paid", "a"), L("invoice", "voided", "b"), L("invoice", "paidoff", "c")]).Paid.ShouldBeTrue();
        LexwareStatusRules.InvoicesPaid([L("invoice", "paid", "a"), L("creditnote", "open", "g")]).Paid.ShouldBeFalse();
        LexwareStatusRules.InvoicesPaid([L("invoice", "draft", "a")]).Paid.ShouldBeFalse();
        LexwareStatusRules.QuotationAccepted([L("quotation", "accepted", "q")]).ShouldBeTrue();
    }

    [Fact]
    public async Task AutoStatus_AcceptedQuotationPlansInquiry_PaidInvoicesRequestReview_OnlyForward()
    {
        await ConfigureAsync(null, "lexwareStatus");
        using var office = await _factory.CreateClientAsAsync("bauleitung", StandardRoles.OfficeId);
        var officeId = await DbAsync(db => db.Set<User>().Where(u => u.UserName == "bauleitung").Select(u => u.Id).SingleAsync(Ct));
        var (customer, contact) = await LinkedCustomerAsync();
        var bauakte = await BauakteAsync(customer, "inquiry");
        await DbAsync(async db =>
        {
            var entity = await db.Set<Bauakte>().SingleAsync(b => b.Id == bauakte, Ct);
            entity.AssignResponsible(officeId);
            return await db.SaveChangesAsync(Ct);
        });
        var quotation = _lexware.AddVoucher("quotation", "open", "AG0001", "2026-11-01", "2026-12-01", 1000m, 1190m, contact);
        var invoice = _lexware.AddVoucher("invoice", "open", "RE0001", "2026-11-20", "2026-12-04", 1000m, 1190m, contact);
        foreach (var (lexwareId, type) in new[] { (quotation, "quotation"), (invoice, "invoice") })
        {
            await SendAsync<LexwareVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign", new { lexwareId, type },
                HttpStatusCode.Created);
        }

        Task<JsonObject> Detail() => SendAsync<JsonObject>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}");
        await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true");
        (await Detail())["status"]!.GetValue<string>().ShouldBe("inquiry");

        _lexware.Vouchers[quotation]["voucherStatus"] = "accepted";
        await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true");
        var planned = await Detail();
        planned["status"]!.GetValue<string>().ShouldBe("planned");
        planned["reviewRequestedAt"].ShouldBeNull();

        _lexware.Vouchers[invoice]["voucherStatus"] = "paid";
        await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true");
        var review = await Detail();
        review["status"]!.GetValue<string>().ShouldBe("planned"); // Abschließen nie automatisch
        review["reviewRequestedAt"].ShouldNotBeNull();
        review["reviewReason"]!.GetValue<string>().ShouldBe("Rechnungen vollständig bezahlt, bitte prüfen und abschließen");
        var listed = await SendAsync<JsonObject>(_admin, HttpMethod.Get, "/api/v1/bauakten?review=true");
        listed["items"]!.AsArray().ShouldHaveSingleItem()!["id"]!.GetValue<Guid>().ShouldBe(bauakte);

        var history = await _admin.GetStringAsync(new Uri($"/api/v1/bauakten/{bauakte}/history", UriKind.Relative), Ct);
        history.ShouldContain("Automatisch durch Lexware Office");
        var inbox = await office.GetStringAsync(new Uri("/api/v1/notifications/", UriKind.Relative), Ct);
        inbox.ShouldContain("Angebot angenommen");
        inbox.ShouldContain("Zur Abschlussprüfung");

        // Zurückstellen: beim gleichen Zahlstand nicht erneut, bei geändertem Stand wieder.
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/review/dismiss");
        await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true");
        (await Detail())["reviewRequestedAt"].ShouldBeNull();
        var second = _lexware.AddVoucher("invoice", "paid", "RE0002", "2026-11-25", "2026-12-09", 200m, 238m, contact);
        await SendAsync<LexwareVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign", new { lexwareId = second, type = "invoice" },
            HttpStatusCode.Created);
        await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true");
        (await Detail())["reviewRequestedAt"].ShouldNotBeNull();

        // Nur vorwärts: Eine von Hand weitergeschaltete Bauakte bleibt, ein angenommenes Angebot setzt nichts zurück.
        var other = await BauakteAsync(customer, "inquiry");
        await DbAsync(async db =>
        {
            (await db.Set<Bauakte>().SingleAsync(b => b.Id == other, Ct)).ChangeStatus(BauakteStatus.InProgress);
            return await db.SaveChangesAsync(Ct);
        });
        var accepted = _lexware.AddVoucher("quotation", "accepted", "AG0002", "2026-11-01", null, 10m, 11.9m, contact);
        await SendAsync<LexwareVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/bauakten/{other}/lexware/vouchers/assign", new { lexwareId = accepted, type = "quotation" },
            HttpStatusCode.Created);
        await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{other}/lexware?refresh=true");
        (await SendAsync<JsonObject>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{other}"))["status"]!.GetValue<string>().ShouldBe("inProgress");
    }

    [Fact]
    public async Task AutoStatus_RulesSwitchable_AndModuleOffDoesNothing()
    {
        await ConfigureAsync(new { statusOnInvoicesPaid = false });
        var (customer, contact) = await LinkedCustomerAsync();
        var bauakte = await BauakteAsync(customer, "inquiry");
        var quotation = _lexware.AddVoucher("quotation", "accepted", "AG0003", "2026-11-01", null, 10m, 11.9m, contact);
        await SendAsync<LexwareVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign", new { lexwareId = quotation, type = "quotation" },
            HttpStatusCode.Created);
        await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true");
        (await SendAsync<JsonObject>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}"))["status"]!.GetValue<string>().ShouldBe("inquiry"); // Modul aus

        await ModulesAsync("lexwareStatus");
        var settings = await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware");
        settings.StatusOnInvoicesPaid.ShouldBeFalse();
        settings.StatusOnQuotationAccepted.ShouldBeTrue();
        await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true");
        (await SendAsync<JsonObject>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}"))["status"]!.GetValue<string>().ShouldBe("planned");
        (await DbAsync(db => db.Set<Bauakte>().AsNoTracking().SingleAsync(b => b.Id == bauakte, Ct))).ReviewRequestedAt.ShouldBeNull();
    }
}
