using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Desktop;
using EstrichOne.Desktop.ViewModels;

namespace EstrichOne.Client.Tests;

/// <summary>ViewModels der Windows-App gegen den echten Server – die WinUI-Oberfläche bindet nur noch an diese Klassen.</summary>
public sealed class ViewModelTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private AppSession NewSession(ISettingsStore? settings = null) =>
        new(settings ?? new MemorySettingsStore(), new MemoryTokenStore(),
            (url, tokens) => new EstrichOneClient(url, tokens, _factory.Server.CreateHandler(), "Testrechner"));

    private async Task<AppSession> SignedInAsync()
    {
        await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        var session = NewSession();
        await session.ConnectAsync("http://localhost", Ct);
        await session.SignInAsync("buero", AuthClient.DefaultPassword, Ct);
        return session;
    }

    private static async Task<T> PutAsync<T>(HttpClient http, string path, object body)
    {
        using var response = await http.PutAsJsonAsync(path, body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(EstrichOneClient.Json, Ct))!;
    }

    [Theory]
    [InlineData("estrich.firma.de", "https://estrich.firma.de/")]
    [InlineData(" https://estrich.firma.de:8443/api/v1/ ", "https://estrich.firma.de:8443/")]
    [InlineData("http://192.168.1.10:8080", "http://192.168.1.10:8080/")]
    public void ServerAddress_IsNormalized(string input, string expected) =>
        AppSession.NormalizeAddress(input).ToString().ShouldBe(expected);

    [Fact]
    public void ServerAddress_Invalid_IsRejectedWithText() =>
        Should.Throw<ArgumentException>(() => AppSession.NormalizeAddress("ftp://x")).Message.ShouldStartWith("Die Serveradresse ist ungültig.");

    [Fact]
    public async Task SignIn_ConnectsServer_ShowsReadableError_AndRemembersServerAndLogin()
    {
        await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        var settings = new MemorySettingsStore();
        using var session = NewSession(settings);
        var vm = new SignInViewModel(session) { ServerAddress = "http://localhost" };
        var signedIn = false;
        vm.SignedIn += (_, _) => signedIn = true;

        await vm.ConnectCommand.ExecuteAsync(null);
        vm.Login = "buero";
        vm.Password = "falsch-falsch";
        await vm.SignInCommand.ExecuteAsync(null);
        var error = vm.Error;
        vm.Password = AuthClient.DefaultPassword;
        await vm.SignInCommand.ExecuteAsync(null);

        vm.IsConnected.ShouldBeTrue();
        vm.ServerName.ShouldBe("Estrichbau Test GmbH");
        error.ShouldBe("Anmeldename oder Passwort ist nicht korrekt.");
        signedIn.ShouldBeTrue();
        vm.Password.ShouldBeEmpty();
        settings.Read(AppSession.ServerKey).ShouldBe("http://localhost/");
        settings.Read(AppSession.LoginKey).ShouldBe("buero");
        new SignInViewModel(session).Login.ShouldBe("buero");
    }

    [Fact]
    public async Task Bauakten_SearchAndStatusFilter_ShowFormattedRows()
    {
        using var session = await SignedInAsync();
        var client = session.Client;
        await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Kraus", "Ahornweg 3", "84028", "Landshut"), Ct);
        await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "Halle Lang", City: "Ergolding", Status: BauakteStatus.Inquiry), Ct);
        var vm = new BauaktenViewModel(session);

        await vm.LoadCommand.ExecuteAsync(null);
        var all = vm.Items.ToList();
        vm.Search = "kraus";
        await vm.LoadCommand.ExecuteAsync(null);
        var found = vm.Items.ToList();
        vm.Search = string.Empty;
        vm.Status = BauaktenViewModel.StatusOptions.Single(o => o.Status == BauakteStatus.Inquiry);
        await vm.LoadCommand.ExecuteAsync(null);

        all.Count.ShouldBe(2);
        found.ShouldHaveSingleItem().Address.ShouldBe("Ahornweg 3, 84028 Landshut");
        vm.Items.ShouldHaveSingleItem().StatusLabel.ShouldBe("Anfrage");
        vm.Summary.ShouldBe("1 Bauakten");
        vm.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Bauakte_NotesWorkItemsAndStatus_AreSavedOnServer()
    {
        using var session = await SignedInAsync();
        var created = await session.Client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Kraus", City: "Landshut"), Ct);
        var vm = new BauakteViewModel(session, created.Id);

        await vm.LoadCommand.ExecuteAsync(null);
        vm.NewNote = "Kunde wünscht Ausführung ab KW 46";
        await vm.AddNoteCommand.ExecuteAsync(null);
        vm.NewWorkItem = "Randdämmstreifen fehlt im Bad";
        vm.NewWorkItemIsDefect = true;
        await vm.AddWorkItemCommand.ExecuteAsync(null);
        await vm.ToggleWorkItemCommand.ExecuteAsync(vm.WorkItems[0]);
        await vm.SetStatusCommand.ExecuteAsync(BauakteStatus.Completed);

        vm.Error.ShouldBeNull();
        vm.Heading.ShouldEndWith("· EFH Kraus");
        vm.Notes.ShouldHaveSingleItem().Author.ShouldBe("Max Huber");
        vm.NewNote.ShouldBeEmpty();
        vm.WorkItems.ShouldHaveSingleItem().Kind.ShouldBe("Mangel");
        vm.WorkItems[0].IsDone.ShouldBeTrue();
        vm.StatusLabel.ShouldBe("Abgeschlossen");
        vm.CanComplete.ShouldBeFalse();
        (await session.Client.GetNotesAsync(created.Id, Ct)).ShouldHaveSingleItem();
        (await session.Client.GetBauakteAsync(created.Id, Ct)).Status.ShouldBe(BauakteStatus.Completed);
    }

    [Fact]
    public async Task Planning_WeekBoard_GroupsByCrew_AndMovesBetweenWeeks()
    {
        using var session = await SignedInAsync();
        using var planner = await _factory.CreateClientAsAsync("planer", StandardRoles.OfficeId);
        var bauakte = await session.Client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Kraus", City: "Landshut"), Ct);
        var crewId = Guid.CreateVersion7();
        await PutAsync<Crew>(planner, $"/api/v1/planning/crews/{crewId}", new { name = "Kolonne Nord", color = "#1677FF", memberUserIds = Array.Empty<Guid>() });
        var vm = new PlanningViewModel(session, _factory.Clock);
        await vm.LoadCommand.ExecuteAsync(null);
        var monday = vm.WeekStart;
        await PutAsync<Assignment>(planner, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = bauakte.Id, day = monday.AddDays(1), startTime = "07:00:00", endTime = "15:30:00", activity = "Estrich einbringen EG",
            crewId, kind = "screed",
        });

        await vm.LoadCommand.ExecuteAsync(null);
        var row = vm.Rows.Single(r => r.Name == "Kolonne Nord");
        var card = row.Cells[1].Items.ShouldHaveSingleItem();
        await vm.NextCommand.ExecuteAsync(null);

        vm.Error.ShouldBeNull();
        monday.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        vm.Days.Count.ShouldBe(5);
        card.Time.ShouldBe("07:00–15:30");
        card.Kind.ShouldBe("Estrich / Ausführung");
        card.Title.ShouldEndWith("· EFH Kraus");
        vm.WeekStart.ShouldBe(monday.AddDays(7));
        vm.Rows.Single(r => r.Name == "Kolonne Nord").Cells.ShouldAllBe(c => c.Items.Count == 0);
        vm.ShowWeekend = true;
        await vm.LoadCommand.ExecuteAsync(null);
        vm.Days.Count.ShouldBe(7);
    }

    [Fact]
    public async Task QuickCall_IsSaved_AppearsAsDue_AndFollowUpCanBeCompleted()
    {
        using var session = await SignedInAsync();
        var vm = new CallsViewModel(session, _factory.Clock);
        vm.SaveQuickCallCommand.CanExecute(null).ShouldBeFalse();

        vm.Caller = "Herr Bauer";
        vm.Phone = "0871 123456";
        vm.Notes = "Fragt nach Termin für Erstbegehung";
        vm.FollowUp = true;
        await vm.SaveQuickCallCommand.ExecuteAsync(null);
        var saved = vm.Saved;
        vm.OnlyDue = true;
        await vm.LoadCommand.ExecuteAsync(null);
        var due = vm.Items.ShouldHaveSingleItem();
        await vm.CompleteFollowUpCommand.ExecuteAsync(due);

        saved.ShouldBe("Gesprächsnotiz „Herr Bauer“ gespeichert.");
        due.Due.ShouldBe("Wiedervorlage");
        due.Handler.ShouldBe("Max Huber");
        vm.Caller.ShouldBeEmpty();
        vm.Items.ShouldBeEmpty();
        var today = new TodayViewModel(session, _factory.Clock);
        await today.LoadCommand.ExecuteAsync(null);
        today.DueCalls.ShouldBeEmpty();
        today.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Orders_OpenFilter_AndDeliveryIsRecorded()
    {
        using var session = await SignedInAsync();
        using var office = await _factory.CreateClientAsAsync("einkauf", StandardRoles.OfficeId);
        var supplierId = Guid.CreateVersion7();
        await PutAsync<Supplier>(office, $"/api/v1/suppliers/{supplierId}", new { id = supplierId, name = "Baustoff Huber KG" });
        var orderId = Guid.CreateVersion7();
        await PutAsync<MaterialOrder>(office, $"/api/v1/material-orders/{orderId}", new
        {
            id = orderId, supplierId, lines = new[] { new { id = Guid.CreateVersion7(), description = "Folie PE 0,2 mm", quantity = 2, unit = "Rolle", unitPrice = 39.5m } },
        });
        var vm = new OrdersViewModel(session);

        await vm.LoadCommand.ExecuteAsync(null);
        var openBefore = vm.Items.Count;
        vm.Filter = OrdersViewModel.Filters.Single(f => f.Key == "draft");
        await vm.LoadCommand.ExecuteAsync(null);
        var draft = vm.Items.ShouldHaveSingleItem();
        draft.CanOrder.ShouldBeTrue();
        await vm.MarkOrderedCommand.ExecuteAsync(draft);
        draft.CanDeliver.ShouldBeTrue();
        await vm.MarkDeliveredCommand.ExecuteAsync(draft);

        openBefore.ShouldBe(0);
        vm.Error.ShouldBeNull();
        draft.Status.ShouldBe("Geliefert");
        draft.Total.ShouldBe("79,00 €");
        draft.Lines.ShouldBe("2 Rolle Folie PE 0,2 mm");
        draft.CanDeliver.ShouldBeFalse();
    }

    [Fact]
    public async Task ReportProblem_CreatesTicket_VisibleInOwnList()
    {
        await _factory.CreateUserAsync("baustelle", roleIds: StandardRoles.SiteWorkerId);
        using var session = NewSession();
        await session.ConnectAsync("http://localhost", Ct);
        await session.SignInAsync("baustelle", AuthClient.DefaultPassword, Ct);
        var vm = new TicketsViewModel(session) { ProblemTitle = "Drucker im Büro druckt nicht", Blocked = true };

        await vm.ReportCommand.ExecuteAsync(null);

        vm.Error.ShouldBeNull();
        vm.IsAgent.ShouldBeFalse();
        vm.Reported.ShouldNotBeNull().ShouldStartWith("Meldung ");
        vm.Items.ShouldHaveSingleItem().Title.ShouldBe("Drucker im Büro druckt nicht");
        vm.ProblemTitle.ShouldBeEmpty();
    }
}
