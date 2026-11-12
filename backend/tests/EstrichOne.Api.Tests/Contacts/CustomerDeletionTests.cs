using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Audit;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Contacts;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Contacts;

/// <summary>
/// Kunden löschen, archivieren, reaktivieren und anonymisieren: Rechte, Bezüge (Bauakten, Projektbeteiligte, Anrufe),
/// Bestätigung, Tombstone im Sync, Kundennummer wird nicht erneut vergeben, Audit.
/// </summary>
public sealed class CustomerDeletionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _office = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
    }

    public async ValueTask DisposeAsync()
    {
        _office.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<ContactResponse> CreateAsync(string name, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["id"] = Guid.CreateVersion7(), ["kind"] = "person", ["name"] = name, ["phone"] = "0871 12345", ["email"] = "kunde@example.org", ["notes"] = "Zahlt bar" };
        if (extra is not null)
        {
            foreach (var p in extra.GetType().GetProperties())
            {
                body[p.Name] = p.GetValue(extra);
            }
        }

        using var response = await _office.PostAsJsonAsync("/api/v1/contacts", body, TestJson.Options, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, Ct))!;
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, ContactResponse c, string? confirm) =>
        client.DeleteAsync(new Uri($"/api/v1/contacts/{c.Id}" + (confirm is null ? "" : "?confirm=" + Uri.EscapeDataString(confirm)), UriKind.Relative), Ct);

    [Fact]
    public async Task CustomerWithoutReferences_IsDeletedWithPersons_TombstoneSynced_NumberNeverReused()
    {
        var customer = await CreateAsync("Erika Probe");
        customer.CustomerNumber.ShouldBe("600001");
        (await _office.PutAsJsonAsync($"/api/v1/contacts/{customer.Id}/persons/{Guid.CreateVersion7()}", new { name = "Sohn Probe", phone = "0171 1" }, TestJson.Options, Ct))
            .EnsureSuccessStatusCode();
        var (_, cursor) = await _office.PullUntilAsync(null, all => all.Any(c => c.Id == customer.Id));

        var references = (await _office.GetFromJsonAsync<CustomerReferencesResponse>($"/api/v1/contacts/{customer.Id}/references", TestJson.Options, Ct))!;
        references.Deletable.ShouldBeTrue();
        references.References.ShouldBeEmpty();
        references.ConfirmText.ShouldBe("600001");

        // Ohne bzw. mit falscher Bestätigung passiert nichts.
        using (var missing = await DeleteAsync(_office, customer, null))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await missing.ReadProblemAsync()).Code.ShouldBe("CONFIRMATION_MISMATCH");
        }

        (await DeleteAsync(_office, customer, "600002")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await DeleteAsync(_office, customer, "600001")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _office.GetAsync(new Uri($"/api/v1/contacts/{customer.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _office.GetFromJsonAsync<List<ContactResponse>>("/api/v1/contacts?status=all&search=Probe", TestJson.Options, Ct))!.ShouldBeEmpty();
        (await _office.GetFromJsonAsync<List<ContactPersonResponse>>($"/api/v1/contacts/{customer.Id}/persons", TestJson.Options, Ct))!.ShouldBeEmpty();
        (await DeleteAsync(_office, customer, "600001")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Apps: Tombstone im Änderungsfeed, ohne Inhalte.
        var (changes, _) = await _office.PullUntilAsync(cursor, all => all.Any(c => c.Id == customer.Id));
        var tombstone = changes.Last(c => c.Id == customer.Id);
        tombstone.EntityType.ShouldBe("contact");
        tombstone.Deleted.ShouldBeTrue();
        tombstone.Data.ShouldBeNull();

        // Die Nummer bleibt vergeben: weder automatisch noch von Hand erneut.
        (await CreateAsync("Nachfolger")).CustomerNumber.ShouldBe("600002");
        using (var manual = await _office.PostAsJsonAsync("/api/v1/contacts", new { id = Guid.CreateVersion7(), kind = "person", name = "Hand", customerNumber = "600001" }, TestJson.Options, Ct))
        {
            manual.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        // Neu anlegen mit derselben ID (z. B. App mit altem Stand) ist nicht möglich.
        using (var again = await _office.PostAsJsonAsync("/api/v1/contacts", new { id = customer.Id, kind = "person", name = "Erika Probe" }, TestJson.Options, Ct))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        var audit = (await admin.GetFromJsonAsync<AuditLogPage>("/api/v1/audit?action=contact.deleted", TestJson.Options, Ct))!;
        var entry = audit.Items.ShouldHaveSingleItem();
        entry.EntityId.ShouldBe(customer.Id.ToString());
        entry.Details["customerNumber"].ShouldBe("600001");
        entry.Details["persons"].ShouldBe("1");
        entry.Details.Values.ShouldNotContain("Erika Probe");
    }

    [Fact]
    public async Task CustomerWithReferences_CannotBeDeleted_ButArchivedAndReactivated()
    {
        var customer = await CreateAsync("Familie Bezug");
        using (var created = await _office.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "EFH Bezug", customerId = customer.Id }, TestJson.Options, Ct))
        {
            created.EnsureSuccessStatusCode();
        }

        var callId = Guid.CreateVersion7();
        using (var call = await _office.PutAsJsonAsync($"/api/v1/calls/{callId}", new { id = callId, kind = "call", customerId = customer.Id, subject = "Rückruf" }, TestJson.Options, Ct))
        {
            call.EnsureSuccessStatusCode();
        }

        var references = (await _office.GetFromJsonAsync<CustomerReferencesResponse>($"/api/v1/contacts/{customer.Id}/references", TestJson.Options, Ct))!;
        references.Deletable.ShouldBeFalse();
        references.References.Select(r => (r.Kind, r.Count)).ShouldBe([("bauakten", 1), ("calls", 1)]);
        references.References[0].Link.ShouldBe($"/bauakten?kunde={customer.Id}");
        references.References[0].Items.ShouldHaveSingleItem().Text.ShouldContain("EFH Bezug");

        using (var refused = await DeleteAsync(_office, customer, customer.CustomerNumber))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var json = await refused.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options, Ct);
            json.GetProperty("code").GetString().ShouldBe("CUSTOMER_HAS_REFERENCES");
            json.GetProperty("detail").GetString()!.ShouldContain("1 Bauakte und 1 Anruf oder Anfrage");
            json.GetProperty("references").GetArrayLength().ShouldBe(2);
        }

        // Archivieren: verschwindet aus Suche und Auswahllisten, bleibt aber erreichbar und mit der Bauakte verknüpft.
        using (var archived = await _office.PostAsync(new Uri($"/api/v1/contacts/{customer.Id}/archive", UriKind.Relative), null, Ct))
        {
            archived.EnsureSuccessStatusCode();
            (await archived.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, Ct))!.ArchivedAt.ShouldNotBeNull();
        }

        (await _office.GetFromJsonAsync<List<ContactResponse>>("/api/v1/contacts?search=Bezug", TestJson.Options, Ct))!.ShouldBeEmpty();
        (await _office.GetFromJsonAsync<List<ContactResponse>>("/api/v1/contacts?search=Bezug&status=archived", TestJson.Options, Ct))!.ShouldHaveSingleItem();
        (await _office.GetFromJsonAsync<List<ContactResponse>>("/api/v1/contacts?search=Bezug&status=all", TestJson.Options, Ct))!.ShouldHaveSingleItem();
        (await _office.GetFromJsonAsync<ContactResponse>($"/api/v1/contacts/{customer.Id}", TestJson.Options, Ct))!.Name.ShouldBe("Familie Bezug");
        var page = (await _office.GetFromJsonAsync<BauaktePage>($"/api/v1/bauakten?customerId={customer.Id}&status=all", TestJson.Options, Ct))!;
        page.Items.ShouldHaveSingleItem();

        using (var reactivated = await _office.PostAsync(new Uri($"/api/v1/contacts/{customer.Id}/reactivate", UriKind.Relative), null, Ct))
        {
            (await reactivated.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, Ct))!.ArchivedAt.ShouldBeNull();
        }

        (await _office.GetFromJsonAsync<List<ContactResponse>>("/api/v1/contacts?search=Bezug", TestJson.Options, Ct))!.ShouldHaveSingleItem();

        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        (await admin.GetFromJsonAsync<AuditLogPage>("/api/v1/audit?action=contact.archived", TestJson.Options, Ct))!.Items.ShouldHaveSingleItem();
        (await admin.GetFromJsonAsync<AuditLogPage>("/api/v1/audit?action=contact.reactivated", TestJson.Options, Ct))!.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DeletedBauakteInTrash_StillCountsAsReference_PartyToo()
    {
        var customer = await CreateAsync("Papierkorb Kunde");
        var other = await CreateAsync("Architekt Plan");
        var id = Guid.CreateVersion7();
        (await _office.PostAsJsonAsync("/api/v1/bauakten", new { id, title = "Gelöschte Akte", customerId = customer.Id }, TestJson.Options, Ct)).EnsureSuccessStatusCode();
        (await _office.PutAsJsonAsync($"/api/v1/bauakten/{id}/parties/{Guid.CreateVersion7()}",
            new { role = "architect", contactId = other.Id, name = "Architekt Plan" }, TestJson.Options, Ct)).EnsureSuccessStatusCode();
        (await _office.PushAsync(SyncClient.Op("bauakte.delete", id, new { }))).Results.ShouldHaveSingleItem().Status.ShouldBe(Modules.Sync.Exports.SyncOperationStatus.Applied);

        var references = (await _office.GetFromJsonAsync<CustomerReferencesResponse>($"/api/v1/contacts/{customer.Id}/references", TestJson.Options, Ct))!;
        references.References.ShouldHaveSingleItem().Items.ShouldHaveSingleItem().Text.ShouldContain("Papierkorb");
        (await DeleteAsync(_office, customer, customer.CustomerNumber)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var party = (await _office.GetFromJsonAsync<CustomerReferencesResponse>($"/api/v1/contacts/{other.Id}/references", TestJson.Options, Ct))!;
        party.References.ShouldHaveSingleItem().Kind.ShouldBe("parties");
    }

    [Fact]
    public async Task Permissions_DeleteForOfficeAndAdmin_AnonymizeOnlyAdmin_SiteWorkerNothing()
    {
        var customer = await CreateAsync("Recht Prüfung");
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        (await DeleteAsync(site, customer, customer.CustomerNumber)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await site.PostAsync(new Uri($"/api/v1/contacts/{customer.Id}/archive", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await site.GetAsync(new Uri($"/api/v1/contacts/{customer.Id}/references", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _office.PostAsJsonAsync($"/api/v1/contacts/{customer.Id}/anonymize", new { confirm = customer.CustomerNumber, irreversible = true }, TestJson.Options, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var permissions = (await _office.GetFromJsonAsync<JsonElement>("/api/v1/access/me/permissions", TestJson.Options, Ct)).ToString();
        permissions.ShouldContain("contacts.delete");
        permissions.ShouldNotContain("contacts.anonymize");
    }

    [Fact]
    public async Task Anonymize_ReplacesPersonalData_KeepsReferences_NeedsDoubleConfirmation()
    {
        var customer = await CreateAsync("Max Mustermann");
        var akte = Guid.CreateVersion7();
        (await _office.PostAsJsonAsync("/api/v1/bauakten", new { id = akte, title = "Altbau", customerId = customer.Id }, TestJson.Options, Ct)).EnsureSuccessStatusCode();
        (await _office.PutAsJsonAsync($"/api/v1/bauakten/{akte}/parties/{Guid.CreateVersion7()}",
            new { role = "client", contactId = customer.Id, name = "Max Mustermann", phone = "0171 999" }, TestJson.Options, Ct)).EnsureSuccessStatusCode();
        var callId = Guid.CreateVersion7();
        (await _office.PutAsJsonAsync($"/api/v1/calls/{callId}", new { id = callId, kind = "call", customerId = customer.Id, callerName = "Max Mustermann", callerPhone = "0171 999" }, TestJson.Options, Ct))
            .EnsureSuccessStatusCode();

        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        var url = new Uri($"/api/v1/contacts/{customer.Id}/anonymize", UriKind.Relative);
        (await admin.PostAsJsonAsync(url, new { confirm = customer.CustomerNumber, irreversible = false }, TestJson.Options, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.PostAsJsonAsync(url, new { confirm = "falsch", irreversible = true }, TestJson.Options, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using (var done = await admin.PostAsJsonAsync(url, new { confirm = customer.CustomerNumber, irreversible = true }, TestJson.Options, Ct))
        {
            done.StatusCode.ShouldBe(HttpStatusCode.OK, await done.Content.ReadAsStringAsync(Ct));
            var anonymized = (await done.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, Ct))!;
            anonymized.Name.ShouldBe($"Anonymisierter Kunde {customer.CustomerNumber}");
            anonymized.CustomerNumber.ShouldBe(customer.CustomerNumber);
            anonymized.Phone.ShouldBeNull();
            anonymized.Email.ShouldBeNull();
            anonymized.Notes.ShouldBeNull();
            anonymized.Phones.ShouldBeEmpty();
            anonymized.AnonymizedAt.ShouldNotBeNull();
            anonymized.ArchivedAt.ShouldNotBeNull();
        }

        // Bezüge bleiben, kopierte Daten sind ersetzt.
        var detail = (await _office.GetFromJsonAsync<JsonElement>($"/api/v1/bauakten/{akte}", TestJson.Options, Ct));
        detail.GetProperty("customerId").GetGuid().ShouldBe(customer.Id);
        var parties = (await _office.GetFromJsonAsync<JsonElement>($"/api/v1/bauakten/{akte}/parties", TestJson.Options, Ct)).ToString();
        parties.ShouldNotContain("Mustermann");
        parties.ShouldNotContain("0171 999");
        var calls = (await _office.GetFromJsonAsync<JsonElement>($"/api/v1/calls?customerId={customer.Id}", TestJson.Options, Ct)).ToString();
        calls.ShouldNotContain("Mustermann");
        calls.ShouldContain(callId.ToString());

        // Nicht wieder aktiv setzbar; Audit ohne Namen.
        (await _office.PostAsync(new Uri($"/api/v1/contacts/{customer.Id}/reactivate", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var audit = (await admin.GetFromJsonAsync<AuditLogPage>("/api/v1/audit?action=contact.anonymized", TestJson.Options, Ct))!.Items.ShouldHaveSingleItem();
        audit.Details["references"].ShouldBe("1 Bauakte, 1 Projektbeteiligung, 1 Anruf oder Anfrage");
    }
}
