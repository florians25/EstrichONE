using System.Net;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Client.Tests;

/// <summary>Der Desktop-Client gegen die echte API (im Speicher) und PostgreSQL: Anmeldung, Erneuern, Bauakten, Planung, Helpdesk.</summary>
public sealed class ClientAgainstServerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private EstrichOneClient NewClient(ITokenStore? tokens = null) =>
        new(new Uri("http://localhost"), tokens ?? new MemoryTokenStore(), _factory.Server.CreateHandler(), "Testrechner");

    [Fact]
    public async Task SignIn_AsDesktop_LoadsUserAndPermissions_WithoutDeviceApproval()
    {
        await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        using var client = NewClient();

        var info = await client.GetServerInfoAsync(TestContext.Current.CancellationToken);
        await client.SignInAsync("buero", AuthClient.DefaultPassword, TestContext.Current.CancellationToken);

        info.ServerName.ShouldBe("Estrichbau Test GmbH");
        client.User!.DisplayName.ShouldBe("Max Huber");
        client.Has("bauakten.edit").ShouldBeTrue();
        client.Has("users.manage").ShouldBeFalse();
        (await client.ListBauaktenAsync(cancellationToken: TestContext.Current.CancellationToken)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task WrongPassword_IsReadableError()
    {
        await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        using var client = NewClient();

        var error = await Should.ThrowAsync<ApiException>(() => client.SignInAsync("buero", "falsch-falsch", TestContext.Current.CancellationToken));

        error.Status.ShouldBe(HttpStatusCode.Unauthorized);
        error.Message.ShouldBe("Anmeldename oder Passwort ist nicht korrekt.");
    }

    [Fact]
    public async Task Restore_UsesStoredRefreshToken_AndExpiredAccessTokenIsRenewedTransparently()
    {
        await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        var store = new MemoryTokenStore();
        using (var first = NewClient(store))
        {
            await first.SignInAsync("buero", AuthClient.DefaultPassword, TestContext.Current.CancellationToken);
        }

        using var client = NewClient(store);
        (await client.RestoreAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        client.User!.UserName.ShouldBe("buero");
        var rotated = store.LoadRefreshToken();

        // Zugriffstoken läuft ab (15 Minuten); der nächste Aufruf erneuert einmal und gelingt.
        _factory.Clock.Advance(TimeSpan.FromHours(1));
        var page = await client.ListBauaktenAsync(cancellationToken: TestContext.Current.CancellationToken);

        page.Items.ShouldBeEmpty();
        store.LoadRefreshToken().ShouldNotBe(rotated);
    }

    [Fact]
    public async Task SignOut_EndsSession_AndForgetsToken()
    {
        await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        var store = new MemoryTokenStore();
        using var client = NewClient(store);
        await client.SignInAsync("buero", AuthClient.DefaultPassword, TestContext.Current.CancellationToken);

        await client.SignOutAsync(TestContext.Current.CancellationToken);

        store.LoadRefreshToken().ShouldBeNull();
        client.User.ShouldBeNull();
        using var again = NewClient(store);
        (await again.RestoreAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Bauakte_CreateEditWithVersion_WorkItemsNotesAndHistory()
    {
        await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        using var client = NewClient();
        await client.SignInAsync("buero", AuthClient.DefaultPassword, TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var created = await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Müller", City: "Landshut"), ct);
        var updated = await client.UpdateBauakteAsync(created.Id, created.Version,
            UpdateBauakteRequest.From(created) with { Status = BauakteStatus.InProgress, Street = "Musterstr. 1" }, ct);
        var conflict = await Should.ThrowAsync<ApiException>(() =>
            client.UpdateBauakteAsync(created.Id, created.Version, UpdateBauakteRequest.From(created) with { Title = "Alt" }, ct));
        var task = await client.CreateWorkItemAsync(new CreateWorkItemRequest(Guid.CreateVersion7(), created.Id, WorkItemKind.Defect, "Riss im Flur"), ct);
        await client.SetWorkItemStatusAsync(task.Id, WorkItemStatus.Done, ct);
        await client.AddNoteAsync(created.Id, "Zugang über Garage", ct);

        updated.Status.ShouldBe(BauakteStatus.InProgress);
        updated.Number.ShouldNotBeNull();
        conflict.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await client.GetWorkItemsAsync(created.Id, ct)).Single().Status.ShouldBe(WorkItemStatus.Done);
        (await client.GetNotesAsync(created.Id, ct)).Single().Text.ShouldBe("Zugang über Garage");
        (await client.GetRoomsAsync(created.Id, ct)).Rooms.ShouldBeEmpty();
        var history = await client.GetHistoryAsync(created.Id, ct);
        history.Select(h => h.Text).ShouldContain("Max Huber hat die Bauakte angelegt.");
        history.Select(h => h.Text).ShouldContain("Max Huber hat einen Mangel als behoben markiert.");
        (await client.ListBauaktenAsync(status: BauakteStatus.InProgress, cancellationToken: ct)).Items.Single().Id.ShouldBe(created.Id);
        (await client.ListBauaktenAsync(search: "Müller", cancellationToken: ct)).Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Planning_And_Helpdesk()
    {
        await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        using var client = NewClient();
        var ct = TestContext.Current.CancellationToken;
        await client.SignInAsync("buero", AuthClient.DefaultPassword, ct);
        var today = DateOnly.FromDateTime(_factory.Clock.GetUtcNow().UtcDateTime);

        var range = await client.GetPlanningAsync(today, today.AddDays(6), ct);
        var ticket = await client.ReportProblemAsync(new CreateTicketRequest(Guid.CreateVersion7(), "Drucker druckt nicht", "Seit heute früh",
            TicketCategory.OfficeIt, TicketSource.Desktop, ReporterBlocked: false, Diagnostics: new Dictionary<string, object?> { ["appVersion"] = "0.1.0" }), ct);

        range.Assignments.ShouldBeEmpty();
        (await client.GetCrewsAsync(ct)).ShouldBeEmpty();
        ticket.Summary.Number.ShouldStartWith("T-");
        ticket.Summary.Source.ShouldBe(TicketSource.Desktop);
        (await client.GetMyTicketsAsync(ct)).Single().Title.ShouldBe("Drucker druckt nicht");
    }

    [Fact]
    public async Task Labels_AreGerman()
    {
        Labels.Of(BauakteStatus.Drying).ShouldBe("Belegreif / Trocknung");
        Labels.Of(TicketStatus.WaitingForReporter).ShouldBe("Rückfrage");
        Labels.Area(42.5m).ShouldBe("42,50 m²");
        await Task.CompletedTask;
    }
}
