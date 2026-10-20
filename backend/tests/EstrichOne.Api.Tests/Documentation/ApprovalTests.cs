using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Documentation;

public sealed class ApprovalTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _worker = null!;
    private HttpClient _office = null!;
    private Guid _bauakteId;
    private Guid _officeUserId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        _officeUserId = (await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId)).Id;
        _office = _factory.CreateClient().WithToken((await _factory.CreateClient().LoginOkAsync("buero")).AccessToken);
        using var response = await _worker.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "EFH Freigaben" }, Ct);
        _bauakteId = (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!.Id;
    }

    public async ValueTask DisposeAsync()
    {
        _worker.Dispose();
        _office.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> UploadDocumentAsync(string name, string category)
    {
        var content = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n" + name);
        var id = Guid.CreateVersion7();
        using var announce = await _worker.PostAsJsonAsync("/api/v1/files", new
        {
            id, bauakteId = _bauakteId, kind = "document", originalName = name, contentType = "application/pdf",
            sizeBytes = (long)content.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)), category,
        }, Ct);
        announce.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new("application/octet-stream");
        using var upload = await _worker.PutAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), body, Ct);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        return id;
    }

    [Fact]
    public async Task Approval_RequestedFromPersonOrRole_DecidedWithComment_AndHistoryKept()
    {
        var file = await UploadDocumentAsync("Angebot 2026-17.pdf", "offer");
        var officeUser = _officeUserId;

        using (var requested = await _worker.PostAsJsonAsync($"/api/v1/files/{file}/approvals",
                   new { id = Guid.CreateVersion7(), assigneeUserId = officeUser, message = "Bitte Preis prüfen" }, Ct))
        {
            requested.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        // Nur eine offene Freigabe je Dokument.
        using (var twice = await _worker.PostAsJsonAsync($"/api/v1/files/{file}/approvals", new { id = Guid.CreateVersion7(), assigneeUserId = officeUser }, Ct))
        {
            twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        var inbox = (await _office.GetFromJsonAsync<List<ApprovalResponse>>("/api/v1/approvals", TestJson.Options, Ct))!;
        var pending = inbox.ShouldHaveSingleItem();
        pending.CanDecide.ShouldBeTrue();
        pending.FileName.ShouldBe("Angebot 2026-17.pdf");
        pending.BauakteTitle.ShouldBe("EFH Freigaben");

        // Die Baustelle ist nicht angefragt und hat kein Freigaberecht.
        using (var denied = await _worker.PostAsJsonAsync($"/api/v1/approvals/{pending.Id}/approve", new { comment = "passt" }, Ct))
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var noReason = await _office.PostAsJsonAsync($"/api/v1/approvals/{pending.Id}/reject", new { comment = "" }, Ct))
        {
            noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var rejected = await _office.PostAsJsonAsync($"/api/v1/approvals/{pending.Id}/reject", new { comment = "Rabatt fehlt" }, Ct))
        {
            rejected.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Neue Anfrage an die Rolle „Büro und Bauleitung“.
        using (var again = await _worker.PostAsJsonAsync($"/api/v1/files/{file}/approvals",
                   new { id = Guid.CreateVersion7(), assigneeRoleId = StandardRoles.OfficeId }, Ct))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var roleTask = (await _office.GetFromJsonAsync<List<ApprovalResponse>>("/api/v1/approvals?scope=mine", TestJson.Options, Ct))!.ShouldHaveSingleItem();
        roleTask.AssigneeName.ShouldBe("Rolle Büro und Bauleitung");
        using (var approved = await _office.PostAsJsonAsync($"/api/v1/approvals/{roleTask.Id}/approve", new { comment = "freigegeben" }, Ct))
        {
            approved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var history = (await _worker.GetFromJsonAsync<List<ApprovalResponse>>($"/api/v1/files/{file}/approvals", TestJson.Options, Ct))!;
        history.Select(a => a.Status).ShouldBe([ApprovalStatus.Approved, ApprovalStatus.Rejected]);
        history[1].DecisionComment.ShouldBe("Rabatt fehlt");
        (await _worker.GetFromJsonAsync<List<ApprovalResponse>>("/api/v1/approvals?scope=requested&status=done", TestJson.Options, Ct))!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CentralDocuments_ListCurrentVersions_WithBauakteAndApprovalFilter()
    {
        var plan = await UploadDocumentAsync("Plan EG.pdf", "plan");
        await UploadDocumentAsync("Lieferschein 4711.pdf", "delivery");
        using (var requested = await _worker.PostAsJsonAsync($"/api/v1/files/{plan}/approvals",
                   new { id = Guid.CreateVersion7(), assigneeRoleId = StandardRoles.OfficeId }, Ct))
        {
            requested.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var all = (await _office.GetFromJsonAsync<DocumentSearchResult>("/api/v1/documents", TestJson.Options, Ct))!;
        all.Items.Count.ShouldBe(2);
        all.Facets.Categories["plan"].ShouldBe(1);
        all.Facets.PendingApprovals.ShouldBe(1);
        all.Items.ShouldAllBe(i => i.BauakteTitle == "EFH Freigaben" && i.VersionCount == 1);

        var pending = (await _office.GetFromJsonAsync<DocumentSearchResult>("/api/v1/documents?approval=pending", TestJson.Options, Ct))!;
        pending.Items.ShouldHaveSingleItem().File.OriginalName.ShouldBe("Plan EG.pdf");
        (await _office.GetFromJsonAsync<DocumentSearchResult>("/api/v1/documents?search=4711", TestJson.Options, Ct))!
            .Items.ShouldHaveSingleItem().File.Category.ShouldBe("delivery");
        (await _office.GetFromJsonAsync<DocumentSearchResult>("/api/v1/documents?search=Freigaben", TestJson.Options, Ct))!.Items.Count.ShouldBe(2);
    }
}
