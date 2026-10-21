using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Modules.Organization;
using EstrichOne.Api.Modules.Sync;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Documentation;

/// <summary>Abnahmeprotokoll, Kundenfreigabe, automatische Statuswechsel, Materialmodul abschaltbar, Firmenlogo.</summary>
public sealed class AcceptanceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static SyncOperationResult Single(PushResponse response) => response.Results.ShouldHaveSingleItem();

    private static async Task<Guid> CreateBauakteAsync(HttpClient client, string title = "EFH Kraus")
    {
        var id = Guid.CreateVersion7();
        Single(await client.PushAsync(SyncClient.Op("bauakte.create", id, new { title }))).Status.ShouldBe(SyncOperationStatus.Applied);
        return id;
    }

    private static async Task<BauakteStatus> StatusAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{id}", TestJson.Options, Ct))!.Status;

    private static async Task<List<string>> HistoryAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{id}/history", TestJson.Options, Ct))!.Select(h => h.Text).ToList();

    [Fact]
    public async Task SignedAcceptance_StoresPdfUnderAcceptances_AndCompletesTheBauakte()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateBauakteAsync(office);
        var id = Guid.CreateVersion7();
        var result = Single(await office.PushAsync(SyncClient.Op("acceptance.create", id, new
        {
            bauakteId = bauakte, result = "accepted", clientName = "Herr Kraus", contractorName = "M. Huber", scope = "Heizestrich EG und OG",
            notes = "Alles sauber.\nRestfeuchte im Bad gemessen.",
            clientSignature = new { png = Png, signedAt = _factory.Clock.GetUtcNow() },
            contractorSignature = new { png = "data:image/png;base64," + Png },
        })));
        result.Status.ShouldBe(SyncOperationStatus.Applied);

        var list = await office.GetFromJsonAsync<List<AcceptanceDto>>($"/api/v1/bauakten/{bauakte}/acceptances", TestJson.Options, Ct);
        var protocol = list!.ShouldHaveSingleItem();
        protocol.State.ShouldBe(AcceptanceState.Completed);
        protocol.HasClientSignature.ShouldBeTrue();
        var pdf = await office.GetAsync(new Uri($"/api/v1/files/{protocol.PdfFileId}/content", UriKind.Relative), Ct);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var file = await office.GetFromJsonAsync<FileResponse>($"/api/v1/files/{protocol.PdfFileId}", TestJson.Options, Ct);
        file!.Category.ShouldBe("acceptance");

        (await StatusAsync(office, bauakte)).ShouldBe(BauakteStatus.Completed);
        (await HistoryAsync(office, bauakte)).ShouldContain("Automatisch: Status von „Geplant“ auf „Abgeschlossen“ gesetzt (Auslöser: Abnahme ohne Vorbehalt).");
        (await office.GetAsync(new Uri($"/api/v1/acceptances/{id}/signatures/client", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CustomerApprovesViaLink_WithReservation_BauakteStaysOpen_LinkThenUsed()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateBauakteAsync(office);
        var id = Guid.CreateVersion7();
        Single(await office.PushAsync(SyncClient.Op("acceptance.create", id, new
        {
            bauakteId = bauakte, result = "acceptedWithReservations", contractorSignature = new { png = Png },
        }))).Status.ShouldBe(SyncOperationStatus.Applied);

        using var link = await office.PostAsJsonAsync($"/api/v1/acceptances/{id}/customer-link", new { }, Ct);
        link.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await link.Content.ReadFromJsonAsync<AcceptanceLinkResponse>(TestJson.Options, Ct);
        body!.Emailed.ShouldBeFalse();
        var token = body.Url[(body.Url.LastIndexOf('/') + 1)..];

        using var anonymous = _factory.CreateClient();
        var view = await anonymous.GetFromJsonAsync<PublicAcceptanceView>($"/api/v1/public/acceptances/{token}", TestJson.Options, Ct);
        view!.BauakteTitle.ShouldBe("EFH Kraus");
        view.Result.ShouldBe(AcceptanceResult.AcceptedWithReservations);

        using var noReason = await anonymous.PostAsJsonAsync($"/api/v1/public/acceptances/{token}/decision", new { approve = false, name = "Kraus" }, Ct);
        noReason.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var approved = await anonymous.PostAsJsonAsync($"/api/v1/public/acceptances/{token}/decision",
            new { approve = true, name = "Peter Kraus", signaturePng = Png }, Ct);
        approved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var protocol = (await office.GetFromJsonAsync<List<AcceptanceDto>>($"/api/v1/bauakten/{bauakte}/acceptances", TestJson.Options, Ct))!.Single();
        protocol.State.ShouldBe(AcceptanceState.Completed);
        protocol.CustomerDecisionName.ShouldBe("Peter Kraus");
        protocol.PdfFileId.ShouldNotBeNull();
        (await StatusAsync(office, bauakte)).ShouldNotBe(BauakteStatus.Completed);
        (await HistoryAsync(office, bauakte)).ShouldContain("Der Kunde (Peter Kraus) hat das Abnahmeprotokoll online freigegeben.");
        // Link ist verbraucht.
        (await anonymous.GetAsync(new Uri($"/api/v1/public/acceptances/{token}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CustomerLink_Expires()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateBauakteAsync(office);
        var id = Guid.CreateVersion7();
        Single(await office.PushAsync(SyncClient.Op("acceptance.create", id, new { bauakteId = bauakte, result = "accepted" })));
        var body = await (await office.PostAsJsonAsync($"/api/v1/acceptances/{id}/customer-link", new { }, Ct))
            .Content.ReadFromJsonAsync<AcceptanceLinkResponse>(TestJson.Options, Ct);
        _factory.Clock.Advance(TimeSpan.FromDays(15));
        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync(new Uri($"/api/v1/public/acceptances/{body!.Url[(body.Url.LastIndexOf('/') + 1)..]}", UriKind.Relative), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FirstSiteActivity_StartsWork_UnlessSwitchedToManual()
    {
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        var first = await CreateBauakteAsync(site);
        Single(await site.PushAsync(SyncClient.Op("diary.create", Guid.CreateVersion7(), new { bauakteId = first, day = "2026-10-20", text = "Estrich EG" })));
        (await StatusAsync(site, first)).ShouldBe(BauakteStatus.InProgress);
        (await HistoryAsync(site, first)).ShouldContain("Automatisch: Status von „Geplant“ auf „In Ausführung“ gesetzt (Auslöser: erster Bautagebucheintrag).");

        var settings = await admin.GetFromJsonAsync<SystemSettingsResponse>("/api/v1/settings", TestJson.Options, Ct);
        var manual = settings!.Settings with { Bauakte = settings.Settings.Bauakte with { AutoStart = Modules.Organization.Exports.AutoStartTrigger.Manual } };
        (await admin.PutAsJsonAsync("/api/v1/settings", new { settings = manual, version = settings.Version }, Ct)).EnsureSuccessStatusCode();
        var second = await CreateBauakteAsync(site);
        Single(await site.PushAsync(SyncClient.Op("diary.create", Guid.CreateVersion7(), new { bauakteId = second, day = "2026-10-20", text = "Estrich OG" })));
        (await StatusAsync(site, second)).ShouldBe(BauakteStatus.Planned);
    }

    [Fact]
    public async Task MaterialsModule_CanBeSwitchedOff_DataStays()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        (await office.GetAsync(new Uri("/api/v1/materials", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var settings = await admin.GetFromJsonAsync<SystemSettingsResponse>("/api/v1/settings", TestJson.Options, Ct);
        var off = settings!.Settings with { Modules = settings.Settings.Modules with { Materials = false } };
        var saved = await (await admin.PutAsJsonAsync("/api/v1/settings", new { settings = off, version = settings.Version }, Ct))
            .Content.ReadFromJsonAsync<SystemSettingsResponse>(TestJson.Options, Ct);

        using var denied = await office.GetAsync(new Uri("/api/v1/materials", UriKind.Relative), Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await denied.ReadProblemAsync()).Code.ShouldBe("MODULE_DISABLED");
        (await admin.GetAsync(new Uri("/api/v1/materials", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync("/api/v1/suppliers", new { id = Guid.CreateVersion7(), name = "Maier" }, Ct)).IsSuccessStatusCode.ShouldBeFalse();

        var on = saved!.Settings with { Modules = saved.Settings.Modules with { Materials = true } };
        (await admin.PutAsJsonAsync("/api/v1/settings", new { settings = on, version = saved.Version }, Ct)).EnsureSuccessStatusCode();
        (await office.GetAsync(new Uri("/api/v1/materials", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CompanyLogo_UploadPublicReadDelete()
    {
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync(new Uri("/api/v1/company/logo", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var content = new ByteArrayContent(Convert.FromBase64String(Png));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        using var upload = await admin.PutAsync(new Uri("/api/v1/company/logo", UriKind.Relative), content, Ct);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await upload.Content.ReadFromJsonAsync<CompanyLogoInfo>(TestJson.Options, Ct))!.HasLogo.ShouldBeTrue();
        (await anonymous.GetAsync(new Uri("/api/v1/company/logo", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var junk = new ByteArrayContent("kein Bild"u8.ToArray());
        (await admin.PutAsync(new Uri("/api/v1/company/logo", UriKind.Relative), junk, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.DeleteAsync(new Uri("/api/v1/company/logo", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GeneratedPdfs_AreStoredInTheBauakte_AsVersions()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateBauakteAsync(office);
        (await office.GetAsync(new Uri($"/api/v1/bauakten/{bauakte}/report.pdf", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        (await office.GetAsync(new Uri($"/api/v1/bauakten/{bauakte}/report.pdf", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        Single(await office.PushAsync(SyncClient.Op("inspection.create", Guid.CreateVersion7(), new
        {
            bauakteId = bauakte, kind = "initialInspection", checks = new Dictionary<string, string> { ["substrate"] = "ok" },
        }))).Status.ShouldBe(SyncOperationStatus.Applied);
        using var form = await office.GetAsync(new Uri($"/api/v1/bauakten/{bauakte}/acceptance-form.pdf", UriKind.Relative), Ct);
        form.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");

        var files = await office.GetFromJsonAsync<List<FileResponse>>($"/api/v1/bauakten/{bauakte}/files", TestJson.Options, Ct);
        var reports = files!.Where(f => f.OriginalName == "Bauaktenbericht.pdf").ToList();
        reports.Count.ShouldBe(2);
        reports.ShouldAllBe(f => f.Category == "protocol" && f.Folder == "Berichte");
        reports.ShouldContain(f => f.PreviousVersionId != null);
        files!.ShouldContain(f => f.OriginalName.StartsWith("Erstbegehungsprotokoll", StringComparison.Ordinal) && f.Folder == "Erstbegehung", string.Join(";", files!.Select(f => f.OriginalName + "/" + f.Folder)));

        var tree = await office.GetFromJsonAsync<List<DocumentTreeCustomer>>("/api/v1/documents/tree", TestJson.Options, Ct);
        tree!.SelectMany(c => c.Bauakten).Single(b => b.Id == bauakte).Categories["protocol"].ShouldBe(2);
    }
}
