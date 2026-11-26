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
        "lineItems[0].unitPrice.taxRatePercentage darf nicht leer sein")]
    [InlineData(422, """{"IssueList":[{"i18nKey":"missing_entity","source":"company.name","type":"validation_failure"}]}""", "company.name missing_entity")]
    [InlineData(400, """{"message":"Request is not valid"}""", "Request is not valid")]
    public async Task ValidationErrors_CarryDetails(int status, string body, string detail)
    {
        _fake.Raw["/v1/contacts"] = (status, body);
        using var client = Client();
        var exception = await Should.ThrowAsync<LexwareException>(() => client.CreateContactAsync(_connection,
            new LexwareContactDraft(true, "Test GmbH", null, null, null, null, null, null, null, null, null, null, null, null), Ct));
        exception.Code.ShouldBe("INVALID");
        exception.Message.ShouldContain(detail);
        _fake.Calls.Count.ShouldBe(1); // schreibende Aufrufe werden bei Ablehnung nicht wiederholt
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
        // anlegt oder ändert, und der Transport weist jeden schreibenden Aufruf außer „Kontakt anlegen“ ab.
        var methods = typeof(LexwareClient).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.DeclaredOnly).Select(m => m.Name).Where(n => n != "Dispose").Order().ToList();
        methods.ShouldBe(["CreateContactAsync", "GetContactAsync", "GetVoucherAsync", "ListCustomersAsync", "ListVouchersAsync", "ProfileAsync"]);

        LexwareClient.WriteAllowed(HttpMethod.Post, "/v1/contacts").ShouldBeTrue();
        foreach (var path in new[] { "/v1/invoices", "/v1/quotations", "/v1/credit-notes", "/v1/order-confirmations", "/v1/contacts/abc", "/v1/voucherlist", "/v1/files" })
        {
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
            {
                LexwareClient.WriteAllowed(method, path).ShouldBeFalse($"{method} {path}");
            }

            LexwareClient.WriteAllowed(HttpMethod.Get, path).ShouldBeTrue();
        }

        LexwareClient.WriteAllowed(HttpMethod.Put, "/v1/contacts").ShouldBeFalse();
        LexwareClient.WriteAllowed(HttpMethod.Delete, "/v1/contacts").ShouldBeFalse();
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
        query.ShouldContain("voucherType=invoice,quotation,creditnote,orderconfirmation");
        query.ShouldContain("voucherStatus=any");
        query.ShouldContain("contactId=c1");
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
