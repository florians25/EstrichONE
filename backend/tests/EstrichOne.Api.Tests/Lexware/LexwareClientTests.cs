using System.Diagnostics;
using System.Text.Json.Nodes;
using EstrichOne.Api.Modules.Lexware;
using EstrichOne.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstrichOne.Api.Tests.Lexware;

/// <summary>Lexware-Client gegen einen gefälschten <see cref="HttpMessageHandler"/>: Anmeldung, Fehlerabbildung, Drosselung, Wiederholung, Inhalt der Anfragen.</summary>
public sealed class LexwareClientTests : IDisposable
{
    private readonly FakeLexware _fake = new();
    private readonly ServiceProvider _services;
    private readonly LexwareConnection _connection = new(new Uri("https://api.lexware.io/"), FakeLexware.Key);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LexwareClientTests()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(LexwareClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _fake);
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _fake.Dispose();
    }

    private LexwareClient Client(Action<LexwareOptions>? configure = null)
    {
        var options = new LexwareOptions { MinRequestInterval = TimeSpan.Zero, RetryDelay = TimeSpan.FromMilliseconds(20), MaxRetryDelay = TimeSpan.FromMilliseconds(50) };
        configure?.Invoke(options);
        return new LexwareClient(_services.GetRequiredService<IHttpClientFactory>(), Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<LexwareClient>.Instance);
    }

    [Fact]
    public async Task Profile_SendsBearerKeyAndAcceptJson()
    {
        using var client = Client();
        var profile = await client.ProfileAsync(_connection, Ct);
        profile.CompanyName.ShouldBe("Estrichbau Muster GmbH");
        var call = _fake.Calls.ShouldHaveSingleItem();
        call.Path.ShouldBe("/v1/profile");
        call.Authorization.ShouldBe("Bearer " + FakeLexware.Key);
        call.Accept.ShouldBe("application/json");
    }

    [Fact]
    public async Task WrongKey_IsExplained_AndNotRetried()
    {
        using var client = Client();
        var exception = await Should.ThrowAsync<LexwareException>(() => client.ProfileAsync(_connection with { ApiKey = "falsch" }, Ct));
        exception.Code.ShouldBe("UNAUTHORIZED");
        exception.Message.ShouldContain("API-Schlüssel");
        exception.Message.ShouldNotContain("falsch");
        _fake.Calls.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(402, "NO_API_ACCESS", "Tarif")]
    [InlineData(403, "NO_API_ACCESS", "Tarif")]
    [InlineData(404, "NOT_FOUND", "nicht gefunden")]
    [InlineData(500, "UNAVAILABLE", "nicht verfügbar")]
    public async Task StatusCodes_AreMappedToGermanMessages(int status, string code, string text)
    {
        _fake.Raw["/v1/invoices"] = (status, "{}");
        using var client = Client(o => o.MaxAttempts = 1);
        var exception = await Should.ThrowAsync<LexwareException>(() => client.GetVoucherAsync(_connection, "invoice", "abc", Ct));
        exception.Code.ShouldBe(code);
        exception.Status.ShouldBe(status);
        exception.Message.ShouldContain(text);
    }

    [Theory]
    [InlineData(406, """{"status":406,"message":"Validation failed","details":[{"violation":"NOTNULL","field":"lineItems[0].unitPrice.taxRatePercentage","message":"darf nicht leer sein"}]}""",
        "lineItems[0].unitPrice.taxRatePercentage darf nicht leer sein", "Position 1: Der Steuersatz fehlt.")]
    [InlineData(422, """{"IssueList":[{"i18nKey":"missing_entity","source":"company.name","type":"validation_failure"}]}""", "company.name missing_entity",
        "Der Firmenname fehlt.")]
    [InlineData(400, """{"message":"Request is not valid"}""", "Request is not valid", "Eine Angabe ist ungültig.")]
    public async Task ValidationErrors_CarryDetails(int status, string body, string detail, string german)
    {
        _fake.Raw["/v1/contacts"] = (status, body);
        using var client = Client();
        var exception = await Should.ThrowAsync<LexwareException>(() => client.CreateContactAsync(_connection,
            new LexwareContactDraft(true, "Test GmbH", null, null, null, null, null, null, null, null, null, null, null, null), Ct));
        exception.Code.ShouldBe("INVALID");
        exception.Message.ShouldStartWith("Lexware Office hat die Angaben nicht angenommen.");
        exception.Message.ShouldContain(german);
        exception.Message.ShouldNotContain(detail); // der englische Originaltext steht nur unter „Technische Einzelheiten“
        exception.Technical.ShouldBe(detail);
        _fake.Calls.Count.ShouldBe(1); // schreibende Aufrufe werden bei Ablehnung nicht wiederholt
    }

    [Fact]
    public void Translation_TaxTypeNotAllowed_OffersNet_AndKeepsOriginal()
    {
        var translation = LexwareErrors.Translate("""{"status":400,"error":"Bad Request","path":"/v1/quotations","message":"Tax type 'constructionService13b' not allowed for this organization"}""");
        translation.Code.ShouldBe(LexwareErrors.TaxTypeNotAllowed);
        translation.RejectedTaxType.ShouldBe("constructionService13b");
        translation.Message.ShouldBe("Lexware Office lässt die Steuerart Bauleistung nach § 13b UStG für Ihr Unternehmen nicht zu. Prüfen Sie die Steuereinstellungen in Lexware Office oder senden Sie den Beleg mit der Steuerart Netto.");
        translation.Issues.ShouldHaveSingleItem().Fix.ShouldBe(LexwareErrors.FixTaxTypeNet);
        translation.Technical.ShouldBe("Tax type 'constructionService13b' not allowed for this organization");
        LexwareErrors.TaxTypeText("constructionService13b", "das Angebot").ShouldContain("senden Sie das Angebot mit der Steuerart Netto.");
    }

    [Fact]
    public void Translation_FieldsAndPositions()
    {
        var translation = LexwareErrors.Translate("""
            {"message":"Validation failed","details":[
              {"field":"lineItems[2].unitName","violation":"NOTNULL","message":"must not be null"},
              {"field":"lineItems[0].quantity","message":"must be greater than 0"},
              {"field":"voucherDate","message":"invalid date format"},
              {"field":"address.contactId","message":"Contact not found"}]}
            """);
        translation.Code.ShouldBe("INVALID");
        translation.Issues.Select(i => i.Text).ShouldBe([
            "Position 3: Die Einheit fehlt.", "Position 1: Die Menge ist zu klein oder negativ.", "Das Belegdatum ist ungültig.",
            "Der Kunde (Kontakt in Lexware Office) wurde in Lexware Office nicht gefunden.",
        ]);
        translation.Issues[0].Position.ShouldBe(3);
        translation.Issues[0].Field.ShouldBe("lines.unit");
        translation.Technical!.ShouldContain("lineItems[2].unitName must not be null");
        LexwareErrors.Translate(null).Message.ShouldBe(LexwareErrors.Generic);
    }

    [Fact]
    public async Task Throttled_IsRetriedWithBackoff_ThenSucceeds()
    {
        _fake.Throttle["/v1/profile"] = 2;
        using var client = Client();
        (await client.ProfileAsync(_connection, Ct)).CompanyName.ShouldNotBeNull();
        _fake.Calls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Throttled_Post_IsRetriedToo_BecauseNothingWasProcessed()
    {
        _fake.Throttle["/v1/contacts"] = 1;
        using var client = Client();
        var id = await client.CreateContactAsync(_connection,
            new LexwareContactDraft(true, "Test GmbH", null, null, null, null, null, null, null, null, null, null, null, null), Ct);
        _fake.Contacts.Keys.ShouldBe([id]);
        _fake.CallsTo("POST", "/v1/contacts").Count().ShouldBe(2);
    }

    [Fact]
    public async Task Throttled_GivesUpAfterMaxAttempts_WithClearMessage()
    {
        _fake.Throttle["/v1/profile"] = 10;
        using var client = Client(o => o.MaxAttempts = 3);
        var exception = await Should.ThrowAsync<LexwareException>(() => client.ProfileAsync(_connection, Ct));
        exception.Code.ShouldBe("RATE_LIMITED");
        _fake.Calls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ServerError_OnPost_IsNotRetried()
    {
        _fake.Raw["/v1/contacts"] = (503, "");
        using var client = Client();
        var draft = new LexwareContactDraft(true, "Test GmbH", null, null, null, null, null, null, null, null, null, null, null, null);
        (await Should.ThrowAsync<LexwareException>(() => client.CreateContactAsync(_connection, draft, Ct))).Code.ShouldBe("UNAVAILABLE");
        _fake.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Requests_AreSpacedToTwoPerSecond()
    {
        using var client = Client(o => o.MinRequestInterval = TimeSpan.FromMilliseconds(150));
        var started = Stopwatch.GetTimestamp();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.ProfileAsync(_connection, Ct)));
        Stopwatch.GetElapsedTime(started).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(430));
        _fake.Calls.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Timeout_IsReportedAsUnderstandableError()
    {
        using var slow = new SlowHandler();
        var services = new ServiceCollection();
        services.AddHttpClient(LexwareClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => slow);
        await using var provider = services.BuildServiceProvider();
        using var client = new LexwareClient(provider.GetRequiredService<IHttpClientFactory>(), Microsoft.Extensions.Options.Options.Create(
            new LexwareOptions { MinRequestInterval = TimeSpan.Zero, AttemptTimeout = TimeSpan.FromMilliseconds(50), MaxAttempts = 2 }), NullLogger<LexwareClient>.Instance);
        (await Should.ThrowAsync<LexwareException>(() => client.ProfileAsync(_connection, Ct))).Code.ShouldBe("TIMEOUT");
        slow.Calls.ShouldBe(2);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage();
        }
    }

    [Fact]
    public void Vouchers_AreReadOnly_ClientWritesNothingButContacts()
    {
        // Rechnungen und Angebote entstehen ausschließlich in Lexware Office: Der Client hat keine Methode, die einen Beleg
        // anlegt oder ändert, und der Transport weist jeden schreibenden Aufruf außer „Kontakt anlegen“ und „Kontakt ändern“ ab.
        var methods = typeof(LexwareClient).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.DeclaredOnly).Select(m => m.Name).Where(n => n != "Dispose").Order().ToList();
        methods.ShouldBe(["CreateArticleAsync", "CreateContactAsync", "CreateQuotationDraftAsync", "GetArticleAsync", "GetContactAsync", "GetVoucherAsync",
            "GetVoucherDocumentAsync", "ListArticlesAsync", "ListCustomersAsync", "ListPaymentConditionsAsync", "ListVouchersAsync", "ProfileAsync",
            "UpdateArticleAsync", "UpdateContactAsync"]);

        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/contacts").ShouldBeTrue();
        LexwareClient.WriteAllowed(HttpMethod.Put, "/v1/contacts/abc").ShouldBeTrue();
        foreach (var path in new[]
                 {
                     "/v1/invoices", "/v1/invoices/abc", "/v1/invoices/abc/document", "/v1/invoices/abc/file", "/v1/quotations/abc",
                     "/v1/quotations/abc/document", "/v1/articles/abc/archive",
                     "/v1/credit-notes", "/v1/credit-notes/abc", "/v1/order-confirmations", "/v1/order-confirmations/abc", "/v1/voucherlist", "/v1/files",
                     "/v1/files/abc", "/v1/contacts/abc/archive", "/v1/contacts/", "/v1/event-subscriptions",
                 })
        {
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
            {
                LexwareClient.WriteAllowed(method, path).ShouldBeFalse($"{method} {path}");
            }

            LexwareClient.WriteAllowed(HttpMethod.Get, path).ShouldBeTrue();
        }

        // Artikel: anlegen und einen einzelnen ändern, nie löschen. Angebote: nur als Entwurf anlegen, nie festschreiben oder ändern.
        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/articles").ShouldBeTrue();
        LexwareClient.WriteAllowed(HttpMethod.Put, "/v1/articles/abc").ShouldBeTrue();
        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/quotations").ShouldBeTrue();
        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/quotations", "?finalize=true").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/articles", "?x=1").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Put, "/v1/quotations").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Put, "/v1/articles").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/invoices").ShouldBeFalse();
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Patch, HttpMethod.Delete })
        {
            LexwareClient.WriteAllowed(method, "/v1/articles/abc").ShouldBeFalse($"{method} /v1/articles/abc");
        }

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            LexwareClient.WriteAllowed(method, "/v1/quotations").ShouldBeFalse($"{method} /v1/quotations");
        }

        // Kontakte: anlegen und einen einzelnen ändern, sonst nichts. Gelöscht oder archiviert wird in Lexware Office nie.
        LexwareClient.WriteAllowed(HttpMethod.Put, "/v1/contacts").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Delete, "/v1/contacts").ShouldBeFalse();
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Patch, HttpMethod.Delete })
        {
            LexwareClient.WriteAllowed(method, "/v1/contacts/abc").ShouldBeFalse($"{method} /v1/contacts/abc");
        }
    }

    [Fact]
    public async Task UpdateContact_ReadsFresh_PutsWithVersion_AndKeepsAllOtherFields()
    {
        var id = _fake.AddContact("Huber Bau GmbH", email: "alt@huber.example", phone: "08031 1111", street: "Altweg 1", zip: "83022", city: "Rosenheim",
            vatId: "DE111111111");
        _fake.Contacts[id]["emailAddresses"]!["office"] = new JsonArray("buero@huber.example");
        _fake.Contacts[id]["phoneNumbers"]!["fax"] = new JsonArray("08031 9999");
        _fake.Contacts[id]["addresses"]!["shipping"] = new JsonArray(new JsonObject { ["street"] = "Lager 5", ["zip"] = "83024", ["city"] = "Rosenheim", ["countryCode"] = "DE" });
        _fake.Contacts[id]["company"]!["contactPersons"] = new JsonArray(new JsonObject { ["lastName"] = "Huber", ["primary"] = true });
        var number = _fake.Contacts[id]["roles"]!["customer"]!["number"]!.GetValue<int>();
        using var client = Client();

        var update = await client.UpdateContactAsync(_connection, id, current =>
        {
            current.Version.ShouldBe(1);
            return [new LexwareContactChange(LexwareSyncFields.Street, "Neuweg 2"), new LexwareContactChange(LexwareSyncFields.Email, "neu@huber.example", "alt@huber.example"),
                new LexwareContactChange(LexwareSyncFields.Name, "Huber Bau GmbH & Co. KG"), new LexwareContactChange(LexwareSyncFields.Phone, null, "08031 1111")];
        }, Ct);

        update.Written.ShouldBeTrue();
        update.Contact.Version.ShouldBe(2);
        (update.Contact.Name, update.Contact.Street, update.Contact.PostalCode).ShouldBe(("Huber Bau GmbH & Co. KG", "Neuweg 2", "83022"));
        _fake.Calls.Select(c => $"{c.Method} {c.Path}").ShouldBe([$"GET /v1/contacts/{id}", $"PUT /v1/contacts/{id}"]);
        var sent = JsonNode.Parse(_fake.CallsTo("PUT", "/v1/contacts/").Single().Body!)!;
        sent["version"]!.GetValue<int>().ShouldBe(1); // optimistische Sperre
        sent["id"].ShouldBeNull();
        sent["updatedDate"].ShouldBeNull();

        var stored = _fake.Contacts[id];
        stored["company"]!["name"]!.GetValue<string>().ShouldBe("Huber Bau GmbH & Co. KG");
        stored["addresses"]!["billing"]![0]!.ToJsonString().ShouldBe("""{"street":"Neuweg 2","zip":"83022","city":"Rosenheim","countryCode":"DE"}""");
        stored["emailAddresses"]!.ToJsonString().ShouldBe("""{"business":["neu@huber.example"],"office":["buero@huber.example"]}""");
        // Alles andere geht unverändert zurück: Kundennummer, USt-ID, Lieferanschrift, Ansprechpartner, Notiz, Fax.
        stored["roles"]!["customer"]!["number"]!.GetValue<int>().ShouldBe(number);
        stored["company"]!["vatRegistrationId"]!.GetValue<string>().ShouldBe("DE111111111");
        stored["company"]!["contactPersons"]![0]!["lastName"]!.GetValue<string>().ShouldBe("Huber");
        stored["addresses"]!["shipping"]![0]!["street"]!.GetValue<string>().ShouldBe("Lager 5");
        stored["note"]!.GetValue<string>().ShouldBe("Stammkunde");
        stored["phoneNumbers"]!.ToJsonString().ShouldBe("""{"business":[],"fax":["08031 9999"]}""");
    }

    [Fact]
    public async Task UpdateContact_WithoutChanges_WritesNothing()
    {
        var id = _fake.AddContact("Huber Bau GmbH");
        using var client = Client();
        var update = await client.UpdateContactAsync(_connection, id, _ => [], Ct);
        update.Written.ShouldBeFalse();
        _fake.Calls.ShouldAllBe(c => c.Method == "GET");
    }

    [Fact]
    public async Task UpdateContact_VersionConflict_IsReadAgainAndDecidedAgain()
    {
        var id = _fake.AddContact("Huber Bau GmbH", street: "Altweg 1", zip: "83022", city: "Rosenheim");
        // Zwischen Lesen und Schreiben ändert jemand den Kontakt in Lexware Office: Die Version passt nicht mehr (409).
        _fake.BeforePut = stored =>
        {
            stored["version"] = 2;
            stored["note"] = "inzwischen geändert";
        };
        var seen = new List<int?>();
        using var client = Client();

        var update = await client.UpdateContactAsync(_connection, id, current =>
        {
            seen.Add(current.Version);
            return [new LexwareContactChange(LexwareSyncFields.City, "Kolbermoor")];
        }, Ct);

        seen.ShouldBe([1, 2]);
        update.Written.ShouldBeTrue();
        update.Contact.Version.ShouldBe(3);
        _fake.Calls.Select(c => c.Method).ShouldBe(["GET", "PUT", "GET", "PUT"]);
        _fake.Contacts[id]["note"]!.GetValue<string>().ShouldBe("inzwischen geändert"); // die fremde Änderung bleibt erhalten
        _fake.Contacts[id]["addresses"]!["billing"]![0]!["city"]!.GetValue<string>().ShouldBe("Kolbermoor");
    }

    [Fact]
    public async Task UpdateContact_GivesUpAfterRepeatedConflicts()
    {
        var id = _fake.AddContact("Huber Bau GmbH");
        _fake.Raw["/v1/contacts/" + id] = (409, """{"status":409,"message":"Resource was modified"}""");
        using var client = Client(o => o.UpdateAttempts = 2);
        // Schon das Lesen scheitert hier mit 409; wichtig ist: kein endloser Neuversuch.
        (await Should.ThrowAsync<LexwareException>(() => client.UpdateContactAsync(_connection, id,
            _ => [new LexwareContactChange(LexwareSyncFields.City, "Kolbermoor")], Ct))).Code.ShouldBe("CONFLICT");
        _fake.Calls.Count.ShouldBeLessThanOrEqualTo(4);
    }

    [Fact]
    public void ApplyChanges_SplitsPersonNames_AndCreatesMissingBillingAddress()
    {
        var person = JsonNode.Parse("""{"id":"2","version":4,"roles":{"customer":{"number":10002}},"person":{"salutation":"Frau","firstName":"Inge","lastName":"Muster"}}""")!.AsObject();
        var body = LexwareClient.ApplyChanges(person, [new LexwareContactChange(LexwareSyncFields.Name, "Inge Maria Musterfrau"),
            new LexwareContactChange(LexwareSyncFields.Salutation, null), new LexwareContactChange(LexwareSyncFields.City, "Rosenheim"),
            new LexwareContactChange(LexwareSyncFields.Email, "inge@example.org"), new LexwareContactChange(LexwareSyncFields.VatId, "DE1")]);
        body.ToJsonString().ShouldBe("""{"version":4,"roles":{"customer":{"number":10002}},"person":{"firstName":"Inge Maria","lastName":"Musterfrau"},"addresses":{"billing":[{"countryCode":"DE","city":"Rosenheim"}]},"emailAddresses":{"business":["inge@example.org"]}}""");
        person["person"]!["lastName"]!.GetValue<string>().ShouldBe("Muster"); // das Gelesene bleibt unverändert
    }

    [Theory]
    [InlineData(LexwareDocumentRoute.Auto, true, "file")]
    [InlineData(LexwareDocumentRoute.Auto, false, "file,document,files")]
    [InlineData(LexwareDocumentRoute.File, true, "file")]
    [InlineData(LexwareDocumentRoute.Document, true, "document,files")]
    public async Task VoucherDocument_IsFetchedAsPdf_OnEitherRoute(LexwareDocumentRoute route, bool fileRoute, string expected)
    {
        var invoice = _fake.AddVoucher("invoice", "open", "RE1", "2026-07-15", "2026-07-29", 100m, 119m, "c1", "Huber");
        _fake.FileRoute = fileRoute;
        using var client = Client(o => o.VoucherDocumentRoute = route);

        var document = await client.GetVoucherDocumentAsync(_connection, "invoice", invoice, Ct);

        document.ContentType.ShouldBe("application/pdf");
        document.Content.ShouldBe(FakeLexware.Pdf(invoice));
        _fake.Calls.ShouldAllBe(c => c.Method == "GET");
        _fake.Calls.Select(c => c.Path.Split('/')[^1] is "file" or "document" ? c.Path.Split('/')[^1] : "files").ShouldBe(expected.Split(','));
        _fake.Calls.Where(c => !c.Path.EndsWith("/document", StringComparison.Ordinal)).ShouldAllBe(c => c.Accept == "application/pdf");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VoucherDocument_ForDrafts_IsExplained(bool fileRoute)
    {
        var draft = _fake.AddVoucher("quotation", "draft", null, "2026-07-15", null, 100m, 119m, "c1", "Huber");
        _fake.FileRoute = fileRoute;
        using var client = Client();
        var exception = await Should.ThrowAsync<LexwareException>(() => client.GetVoucherDocumentAsync(_connection, "quotation", draft, Ct));
        exception.Code.ShouldBe("NO_DOCUMENT");
        exception.Message.ShouldBe("Für Entwürfe stellt Lexware Office kein PDF bereit.");
    }

    [Fact]
    public async Task VoucherDocument_MissingVoucher_IsNotFound_AndNonPdfIsRejected()
    {
        using var client = Client();
        (await Should.ThrowAsync<LexwareException>(() => client.GetVoucherDocumentAsync(_connection, "invoice", "gibt-es-nicht", Ct))).Code.ShouldBe("NOT_FOUND");
        _fake.Raw["/v1/invoices/abc/file"] = (200, """{"hinweis":"kein PDF"}""");
        (await Should.ThrowAsync<LexwareException>(() => client.GetVoucherDocumentAsync(_connection, "invoice", "abc", Ct))).Code.ShouldBe("NO_DOCUMENT");
    }

    [Fact]
    public async Task Vouchers_AreReadByType_AndListedForContactOrNumber()
    {
        var invoice = _fake.AddVoucher("invoice", "open", "RE1", "2026-07-15", "2026-07-29", 100m, 119m, "c1", "Huber", introduction: "Bauakte 2026-014");
        var quotation = _fake.AddVoucher("quotation", "accepted", "AG7", "2026-06-01", "2026-06-30", 200m, 238m, "c1", "Huber");
        _fake.AddVoucher("invoice", "paid", "RE2", "2026-05-01", null, 10m, 11.9m, "c2", "Lang");
        using var client = Client();

        var read = await client.GetVoucherAsync(_connection, "invoice", invoice, Ct);
        (read.Type, read.Status, read.Number, read.GrossAmount, read.DueDate).ShouldBe(("invoice", "open", "RE1", 119m, new DateOnly(2026, 7, 29)));
        read.Text!.ShouldContain("Bauakte 2026-014");
        var offer = await client.GetVoucherAsync(_connection, "quotation", quotation, Ct);
        (offer.Type, offer.Status, offer.DueDate).ShouldBe(("quotation", "accepted", new DateOnly(2026, 6, 30)));
        _fake.Calls.Select(c => c.Path).ShouldBe([$"/v1/invoices/{invoice}", $"/v1/quotations/{quotation}"]);
        (await Should.ThrowAsync<LexwareException>(() => client.GetVoucherAsync(_connection, "salesinvoice", invoice, Ct))).Code.ShouldBe("UNKNOWN_VOUCHER_TYPE");

        var ofContact = await client.ListVouchersAsync(_connection, "c1", null, 100, Ct);
        ofContact.Select(v => (v.Type, v.Number)).ShouldBe([("invoice", "RE1"), ("quotation", "AG7")]);
        var query = _fake.Calls.Last().Query;
        query.ShouldContain("voucherType=invoice,quotation,creditnote,orderconfirmation,deliverynote");
        query.ShouldContain("voucherStatus=any");
        query.ShouldContain("contactId=c1");

        // Lieferscheine erscheinen als eigene Belegart (nur lesend); Auftragsbestätigung und Lieferschein sind nie zahlbar.
        var delivery = _fake.AddVoucher("deliverynote", "open", "LS3", "2026-07-02", null, 0m, 0m, "c3", "Moser");
        (await client.ListVouchersAsync(_connection, "c3", null, 100, Ct)).ShouldHaveSingleItem().Type.ShouldBe("deliverynote");
        (await client.GetVoucherAsync(_connection, "deliverynote", delivery, Ct)).Status.ShouldBe("open");
        _fake.Calls.Last().Path.ShouldBe($"/v1/delivery-notes/{delivery}");
        LexwareVoucherTypes.Payable("orderconfirmation").ShouldBeFalse();
        LexwareVoucherTypes.Payable("deliverynote").ShouldBeFalse();
        LexwareVoucherTypes.Payable("invoice").ShouldBeTrue();
        (await client.ListVouchersAsync(_connection, null, "RE2", 25, Ct)).ShouldHaveSingleItem().ContactName.ShouldBe("Lang");
        _fake.Calls.ShouldAllBe(c => c.Method == "GET");
    }

    [Fact]
    public void Contact_IsReadFromCompanyAndPersonShapes()
    {
        var company = LexwareClient.ReadContact(JsonNode.Parse("""
            {"id":"1","roles":{"customer":{"number":10307}},"company":{"name":"Testfirma","vatRegistrationId":"DE123456789"},
             "addresses":{"billing":[{"street":"Hauptstr. 5","zip":"12345","city":"Musterort","countryCode":"DE"}]},
             "emailAddresses":{"business":["business@example.org"],"office":["office@example.org"]},
             "phoneNumbers":{"business":["08000/1231"],"fax":["08000/1233"]},"archived":false}
            """)!.AsObject())!;
        company.IsCompany.ShouldBeTrue();
        company.CustomerNumber.ShouldBe("10307");
        company.Emails.ShouldBe(["business@example.org", "office@example.org"]);
        company.Phones.ShouldBe(["08000/1231"]);
        company.PostalCode.ShouldBe("12345");

        var person = LexwareClient.ReadContact(JsonNode.Parse("""{"id":"2","roles":{"vendor":{}},"person":{"salutation":"Frau","firstName":"Inge","lastName":"Musterfrau"}}""")!.AsObject())!;
        person.IsCompany.ShouldBeFalse();
        person.Name.ShouldBe("Inge Musterfrau");
        person.IsCustomer.ShouldBeFalse();
    }
}
