using System.Net;
using System.Net.Http.Json;
using System.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Desktop;
using EstrichOne.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Client.Tests;

/// <summary>
/// Alle Funktionen, die die Windows-App nativ anbietet (statt ins Web umzuleiten), gegen die echte API und PostgreSQL:
/// Dateien, Bautagebuch/Prüfungen, Räume, Kunden-Neuanlage, Planung, Material, Helpdesk und Verwaltung.
/// </summary>
public sealed class ExtendedClientTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task<EstrichOneClient> SignedInAsync(string userName, params Guid[] roles)
    {
        await _factory.CreateUserAsync(userName, roleIds: roles);
        var client = new EstrichOneClient(new Uri("http://localhost"), new MemoryTokenStore(), _factory.Server.CreateHandler(), "Testrechner");
        await client.SignInAsync(userName, AuthClient.DefaultPassword, Ct);
        return client;
    }

    private async Task<AppSession> SessionAsync(string userName, params Guid[] roles)
    {
        await _factory.CreateUserAsync(userName, roleIds: roles);
        var session = new AppSession(new MemorySettingsStore(), new MemoryTokenStore(),
            (url, tokens) => new EstrichOneClient(url, tokens, _factory.Server.CreateHandler(), "Testrechner"));
        await session.ConnectAsync("http://localhost", Ct);
        await session.SignInAsync(userName, AuthClient.DefaultPassword, Ct);
        return session;
    }

    /// <summary>Ersteinrichtung (Firma und Administrator „admin“) – Voraussetzung für Firmendaten.</summary>
    private async Task CompleteSetupAsync()
    {
        string code;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            code = (await scope.ServiceProvider.GetRequiredService<EstrichOne.Api.Modules.Setup.SetupService>().IssueCodeIfRequiredAsync(Ct))!;
        }

        using var http = _factory.CreateClient();
        using var response = await http.PostAsJsonAsync("/api/v1/setup", new
        {
            setupCode = code, companyName = "Estrichbau Muster GmbH",
            administrator = new { displayName = "Anna Admin", userName = "admin", email = (string?)null, password = AuthClient.DefaultPassword },
        }, Ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Kleinstes gültiges PNG (1×1 Pixel).</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    [Fact]
    public async Task Files_UploadPhotoAndDocument_RenameCategorizeDownloadDelete()
    {
        using var client = await SignedInAsync("buero", StandardRoles.OfficeId);
        var bauakte = await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Kraus"), Ct);
        var progress = new List<double>();

        var photo = await client.UploadFileAsync(Guid.CreateVersion7(), bauakte.Id, "Estrich EG.png", () => new MemoryStream(Png),
            capturedAt: DateTimeOffset.UtcNow.AddHours(-2), progress: new SyncProgress(progress.Add), cancellationToken: Ct);
        var scan = await client.UploadFileAsync(Guid.CreateVersion7(), bauakte.Id, "Lieferschein.png", () => new MemoryStream(Png), category: "delivery",
            kind: FileKind.Document, cancellationToken: Ct);
        var offer = await client.UploadFileAsync(Guid.CreateVersion7(), bauakte.Id, "Angebot 2026-118.pdf", () => new MemoryStream(Pdf), category: "offer",
            cancellationToken: Ct);
        var renamed = await client.UpdateFileAsync(offer.Id, "Angebot (unterschrieben)", "order", Ct);
        using var downloaded = new MemoryStream();
        await client.DownloadFileAsync(offer.Id, downloaded, cancellationToken: Ct);
        var documents = await client.GetFilesAsync(bauakte.Id, FileKind.Document, Ct);
        await client.DeleteFileAsync(scan.Id, Ct);

        photo.Kind.ShouldBe(FileKind.Photo);
        photo.IsStored.ShouldBeTrue();
        photo.CapturedAt.ShouldNotBeNull();
        progress.ShouldNotBeEmpty();
        progress[^1].ShouldBe(1d);
        scan.Kind.ShouldBe(FileKind.Document);
        scan.Category.ShouldBe("delivery");
        offer.ContentType.ShouldBe("application/pdf");
        renamed.DisplayName.ShouldBe("Angebot (unterschrieben)");
        renamed.Category.ShouldBe("order");
        downloaded.ToArray().ShouldBe(Pdf);
        documents.Count.ShouldBe(2);
        (await client.GetFilesAsync(bauakte.Id, Ct)).Select(f => f.Id).ShouldBe([photo.Id, offer.Id], ignoreOrder: true);
        (await client.GetFileContentAsync(photo.Id, "original", Ct)).ShouldBe(Png);
        FileTypes.ContentTypeOf("Plan.DOCX").ShouldStartWith("application/vnd.openxmlformats");
        FileTypes.IsSupported("virus.exe").ShouldBeFalse();
    }

    [Fact]
    public async Task Documents_FoldersVersionsLinksAndZipArchive()
    {
        using var client = await SignedInAsync("buero", StandardRoles.OfficeId);
        var bauakte = await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Kraus"), Ct);
        var rooms = await client.SaveRoomAsync(bauakte.Id, new SaveRoomRequest(Guid.CreateVersion7(), null, "Bad", "EG", 8m, null, 65, null, true,
            RoomStatus.Planned, null), Ct);
        var defect = await client.CreateWorkItemAsync(new CreateWorkItemRequest(Guid.CreateVersion7(), bauakte.Id, WorkItemKind.Defect, "Riss im Bad"), Ct);

        var plan = await client.UploadFileAsync(Guid.CreateVersion7(), bauakte.Id, "Grundriss EG.pdf", () => new MemoryStream(Pdf), category: "plan",
            folder: " Pläne / EG ", cancellationToken: Ct);
        var revised = await client.UploadFileAsync(Guid.CreateVersion7(), bauakte.Id, "Grundriss EG Index B.pdf",
            () => new MemoryStream([.. Pdf, .. "% Index B"u8.ToArray()]), previousVersionId: plan.Id, cancellationToken: Ct);
        var photo = await client.UploadFileAsync(Guid.CreateVersion7(), bauakte.Id, "riss.png", () => new MemoryStream(Png), cancellationToken: Ct);
        // Vorschaubilder entstehen im Hintergrund und ändern den Datensatz; erst danach Angaben ändern.
        for (var i = 0; i < 100 && photo.Variants == VariantState.Pending; i++)
        {
            await Task.Delay(100, Ct);
            photo = await client.GetFileAsync(photo.Id, Ct);
        }

        var linked = await client.UpdateFileDetailsAsync(photo.Id, UpdateFileDetailsRequest.From(photo) with
        {
            Caption = "Riss im Estrich", RoomId = rooms.Rooms.Single().Id, WorkItemId = defect.Id,
        }, Ct);
        var renamed = await client.UpdateFileDetailsAsync(plan.Id, UpdateFileDetailsRequest.From(plan) with { OriginalName = "Grundriss EG Index A.pdf", Folder = "Pläne/Alt" }, Ct);
        using var zip = new MemoryStream();
        await client.DownloadArchiveAsync(bauakte.Id, [revised.Id, photo.Id], zip, Ct);
        zip.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(zip);

        plan.Folder.ShouldBe("Pläne/EG");
        revised.PreviousVersionId.ShouldBe(plan.Id);
        revised.Category.ShouldBe("plan");
        revised.Folder.ShouldBe("Pläne/EG");
        linked.RoomId.ShouldBe(rooms.Rooms.Single().Id);
        linked.WorkItemId.ShouldBe(defect.Id);
        linked.DisplayName.ShouldBe("Riss im Estrich");
        renamed.OriginalName.ShouldBe("Grundriss EG Index A.pdf");
        renamed.Folder.ShouldBe("Pläne/Alt");
        archive.Entries.Select(e => e.FullName).ShouldBe(["Dokumente/Pläne/EG/Grundriss EG Index B.pdf", "Fotos/riss.png"], ignoreOrder: true);

        var diaryId = Guid.CreateVersion7();
        await client.AddDiaryEntryAsync(diaryId, bauakte.Id, new DiaryInput(new DateOnly(2026, 10, 20), "Estrich Bad eingebracht", "sunny", 14, 2,
            new TimeOnly(7, 0), new TimeOnly(15, 30), "Kolonne Nord", [photo.Id]), Ct);
        var diary = (await client.GetDiaryAsync(bauakte.Id, Ct)).ShouldHaveSingleItem();
        diary.StartTime.ShouldBe(new TimeOnly(7, 0));
        diary.Crew.ShouldBe("Kolonne Nord");
        diary.PhotoFileIds.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(photo.Id);
        await client.UpdateDiaryEntryAsync(diaryId, new DiaryInput(diary.Day, diary.Text, "rain", null, 2, new TimeOnly(7, 30), null, null, []), Ct);
        var edited = (await client.GetDiaryAsync(bauakte.Id, Ct)).Single();
        edited.Weather.ShouldBe("rain");
        edited.StartTime.ShouldBe(new TimeOnly(7, 30));
        edited.PhotoFileIds.ShouldNotBeNull().ShouldBeEmpty();
        QrCodes.Png("https://estrich.example/einrichten?code=ABC")[..4].ShouldBe(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' });
    }

    [Fact]
    public async Task Upload_EmptyOrUnsupportedFile_IsReadableError()
    {
        using var client = await SignedInAsync("buero", StandardRoles.OfficeId);
        var bauakte = await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Kraus"), Ct);

        var empty = await Should.ThrowAsync<ArgumentException>(() =>
            client.UploadFileAsync(Guid.CreateVersion7(), bauakte.Id, "leer.pdf", () => new MemoryStream(), cancellationToken: Ct));
        var fake = await Should.ThrowAsync<ApiException>(() =>
            client.UploadFileAsync(Guid.CreateVersion7(), bauakte.Id, "kein.pdf", () => new MemoryStream(Encoding.ASCII.GetBytes("MZ das ist kein PDF")),
                cancellationToken: Ct));

        empty.Message.ShouldContain("leer");
        fake.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Documentation_DiaryAndInspections_AreWrittenFromTheDesktop()
    {
        using var client = await SignedInAsync("buero", StandardRoles.OfficeId);
        var bauakte = await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Kraus"), Ct);
        var today = DateOnly.FromDateTime(_factory.Clock.GetLocalNow().DateTime);
        var diaryId = Guid.CreateVersion7();

        await client.AddDiaryEntryAsync(diaryId, bauakte.Id, today, "Estrich EG eingebracht, 62 m²", "cloudy", 12, 3, Ct);
        await client.UpdateDiaryEntryAsync(diaryId, "Estrich EG eingebracht, 64 m²", "rain", 11, 3, Ct);
        await client.AddInitialInspectionAsync(Guid.CreateVersion7(), new NewInitialInspection(bauakte.Id, _factory.Clock.GetUtcNow().AddHours(-1), false,
            new Dictionary<string, CheckResult> { ["substrate"] = CheckResult.Ok, ["edgeInsulation"] = CheckResult.NotOk }, "Randdämmstreifen fehlt im Bad"), cancellationToken: Ct);
        await client.AddCmMeasurementAsync(Guid.CreateVersion7(), new NewCmMeasurement(bauakte.Id, _factory.Clock.GetUtcNow(), "Wohnen EG", "cement", true, 65,
            1.6m, null), cancellationToken: Ct);
        var bad = await Should.ThrowAsync<ApiException>(() => client.AddCmMeasurementAsync(Guid.CreateVersion7(),
            new NewCmMeasurement(Guid.CreateVersion7(), _factory.Clock.GetUtcNow(), "x", "cement", false, null, 1m, null), cancellationToken: Ct));

        var diary = (await client.GetDiaryAsync(bauakte.Id, Ct)).ShouldHaveSingleItem();
        diary.Text.ShouldBe("Estrich EG eingebracht, 64 m²");
        diary.Weather.ShouldBe("rain");
        Reports.WeatherLine(diary).ShouldBe("Regen · 11 °C · 3 anwesend");
        var inspections = await client.GetInspectionsAsync(bauakte.Id, Ct);
        var initial = inspections.Single(i => i.Kind == InspectionKind.InitialInspection);
        Labels.InspectionResult(initial).ShouldBe("Mit Mängeln – Klärung nötig");
        var cm = inspections.Single(i => i.Kind == InspectionKind.CmMeasurement);
        cm.LimitCmPercent.ShouldNotBeNull();
        cm.IsReadyForCovering.ShouldBe(cm.MeasuredCmPercent <= cm.LimitCmPercent);
        bad.Code.ShouldNotBeNullOrEmpty();
        await client.DeleteDiaryEntryAsync(diaryId, Ct);
        (await client.GetDiaryAsync(bauakte.Id, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task NewBauakte_WithNewCustomer_CreatesCustomerAndCopiesAddress()
    {
        using var session = await SessionAsync("buero", StandardRoles.OfficeId);
        using var vm = new NewBauakteViewModel(session)
        {
            Title = "EFH Kraus, Neubau", Choice = CustomerChoice.New, NewCustomerName = "Familie Kraus", NewCustomerPhone = "0871 4711",
            NewCustomerStreet = "Ahornweg 3", NewCustomerPostalCode = "84028", NewCustomerCity = "Landshut", SiteIsCustomerAddress = true,
        };

        var created = await vm.CreateAsync();

        vm.Error.ShouldBeNull();
        created.ShouldNotBeNull();
        created.Status.ShouldBe(BauakteStatus.Planned);
        created.City.ShouldBe("Landshut");
        var customer = await session.Client.GetContactAsync(created.CustomerId!.Value, Ct);
        customer.Name.ShouldBe("Familie Kraus");
        customer.Phone.ShouldBe("0871 4711");

        // Zweite Bauakte: denselben Kunden über die Suche wählen, als Anfrage.
        using var second = new NewBauakteViewModel(session) { Title = "Garage Kraus", AsInquiry = true, CustomerSearch = "kraus" };
        await second.SearchCustomersAsync();
        second.SelectedCustomer = second.CustomerResults.ShouldHaveSingleItem();
        var inquiry = await second.CreateAsync();

        second.Choice.ShouldBe(CustomerChoice.Existing);
        inquiry!.CustomerId.ShouldBe(customer.Id);
        inquiry.Status.ShouldBe(BauakteStatus.Inquiry);
        (await session.Client.ListBauaktenAsync(customerId: customer.Id, cancellationToken: Ct)).Items.Count.ShouldBe(2);
        new NewBauakteViewModel(session) { Choice = CustomerChoice.New, Title = "X" }.Validate().ShouldBe("Bitte den Namen des neuen Kunden angeben.");
    }

    [Fact]
    public async Task Rooms_SectionsAndRooms_WithSumsAndMaterialRequirements()
    {
        using var client = await SignedInAsync("buero", StandardRoles.OfficeId);
        var bauakte = await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Kraus"), Ct);
        var types = await client.GetScreedTypesAsync(Ct);
        var section = Guid.CreateVersion7();

        await client.SaveSectionAsync(bauakte.Id, section, "Erdgeschoss", cancellationToken: Ct);
        await client.SaveRoomAsync(bauakte.Id, new SaveRoomRequest(Guid.CreateVersion7(), section, "Wohnen", "EG", 40m, types[0].Id, 65, 40, true,
            RoomStatus.Planned, null), Ct);
        var bath = Guid.CreateVersion7();
        await client.SaveRoomAsync(bauakte.Id, new SaveRoomRequest(bath, section, "Bad", "EG", 8.5m, types[0].Id, 65, 40, true, RoomStatus.Laid, null), Ct);
        var overview = await client.SaveRoomAsync(bauakte.Id, new SaveRoomRequest(Guid.CreateVersion7(), null, "Garage", null, 20m, null, null, null, false,
            RoomStatus.Planned, null), Ct);
        var afterDelete = await client.DeleteRoomAsync(bauakte.Id, bath, Ct);
        var noSection = await client.DeleteSectionAsync(bauakte.Id, section, Ct);

        overview.TotalAreaSquareMeters.ShouldBe(68.5m);
        overview.HeatedAreaSquareMeters.ShouldBe(48.5m);
        afterDelete.Rooms.Count.ShouldBe(2);
        noSection.Sections.ShouldBeEmpty();
        noSection.Rooms.ShouldAllBe(r => r.SectionId == null);
        var needs = MaterialRequirements.Calculate(overview, 5);
        var screed = needs.First(n => n.Unit == "m³");
        screed.Amount.ShouldBe(3.5m); // 48,5 m² × 0,065 m = 3,15 m³ + 5 % = 3,31 → 3,5
        screed.Detail.ShouldBe("48,5 m² · 3,15 m³ netto");
        needs.Single(n => n.Key == "ins-40").Amount.ShouldBe(51m);
        needs.ShouldNotContain(n => n.Description == "Estrich (Art offen)"); // Garage ohne Dicke: kein Volumen
    }

    [Fact]
    public async Task Planning_AssignmentsBacklogMoveCrewsResourcesAbsences()
    {
        using var client = await SignedInAsync("buero", StandardRoles.OfficeId);
        var me = client.User!.Id;
        var bauakte = await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Kraus", City: "Landshut"), Ct);
        var crewId = Guid.CreateVersion7();
        var crew = await client.SaveCrewAsync(crewId, new SaveCrewRequest("Kolonne Nord", me, [me], "#174A8B", true, 1), Ct);
        var pump = await client.SaveResourceAsync(Guid.CreateVersion7(), new SaveResourceRequest(ResourceKind.Pump, "Mixokret M740", "LA-E 740", null, true), Ct);
        var monday = PlanningBoard.MondayOf(DateOnly.FromDateTime(_factory.Clock.GetLocalNow().DateTime)).AddDays(7);
        var id = Guid.CreateVersion7();

        var backlogBefore = (await client.GetBacklogAsync(cancellationToken: Ct)).Single(b => b.BauakteId == bauakte.Id);
        var saved = await client.SaveAssignmentAsync(id, new SaveAssignmentRequest(bauakte.Id, monday, new TimeOnly(7, 0), "Estrich einbringen EG", crewId, [],
            [pump.Id], null, AssignmentKind.Screed, new TimeOnly(15, 30)), Ct);
        var absence = await client.SaveAbsenceAsync(Guid.CreateVersion7(), new SaveAbsenceRequest(me, monday.AddDays(1), monday.AddDays(2), AbsenceKind.Vacation, null), Ct);
        var move = PlanningBoard.Move(saved, [crew], crewId, monday.AddDays(1))!;
        var moved = await client.SaveAssignmentAsync(id, move, Ct);
        var range = await client.GetPlanningAsync(monday, monday.AddDays(4), Ct);
        var lanes = PlanningBoard.Lanes(await client.GetCrewsAsync(Ct));

        backlogBefore.UpcomingAssignments.ShouldBe(0);
        (await client.GetBacklogAsync(cancellationToken: Ct)).Single(b => b.BauakteId == bauakte.Id).UpcomingAssignments.ShouldBe(1);
        moved.Day.ShouldBe(monday.AddDays(1));
        moved.Resources.ShouldHaveSingleItem().Name.ShouldBe("Mixokret M740");
        moved.Warnings.ShouldNotBeEmpty(); // Urlaub des Kolonnenmitglieds
        PlanningBoard.InCell(range.Assignments, lanes, crewId, monday.AddDays(1)).ShouldHaveSingleItem();
        PlanningBoard.Move(moved, [crew], crewId, monday.AddDays(1)).ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => PlanningBoard.Move(moved, [crew], null, monday));
        PlanningBoard.VisibleDays(PlanningView.Month, new DateOnly(2026, 10, 20), true).Count.ShouldBe(35);
        PlanningBoard.VisibleDays(PlanningView.Week, new DateOnly(2026, 10, 22), false)[0].ShouldBe(new DateOnly(2026, 10, 19));
        PlanningBoard.Shift(PlanningView.Month, new DateOnly(2026, 1, 31), 1).ShouldBe(new DateOnly(2026, 2, 1));
        await client.DeleteAbsenceAsync(absence.Id, Ct);
        await client.DeleteAssignmentAsync(id, Ct);
        (await client.GetPlanningAsync(monday, monday.AddDays(4), Ct)).Assignments.ShouldBeEmpty();
        (await client.GetResourcesAsync(Ct)).ShouldHaveSingleItem().Kind.ShouldBe(ResourceKind.Pump);
    }

    [Fact]
    public async Task Office_ContactsSuppliersMaterialsOrdersAndInquiryConversion()
    {
        await CompleteSetupAsync();
        using var client = await SignedInAsync("buero", StandardRoles.OfficeId);
        var contact = await client.CreateContactAsync(new SaveContactRequest(Guid.CreateVersion7(), ContactKind.Company, "Bauträger Lang GmbH", "K-1001",
            City: "Ergolding"), Ct);
        var updated = await client.UpdateContactAsync(contact with { Phone = "0871 999" }, Ct);
        var dryRun = await client.ImportContactsAsync("Name;Ort;Telefon\nHuber Bau;Landshut;0871 1\n", true, Ct);
        var supplier = await client.SaveSupplierAsync(new SaveSupplierRequest(Guid.CreateVersion7(), "Baustoff Huber KG", "4711", "Frau Maier", null,
            "bestellung@huber.example", null, null, "Landshut", null), Ct);
        var material = await client.SaveMaterialAsync(new SaveMaterialRequest(Guid.CreateVersion7(), "CT-C25-F4", MaterialCategory.Screed, "t", "Z-25",
            supplier.Id, 89.5m, null), Ct);
        var order = await client.SaveOrderAsync(new SaveOrderRequest(Guid.CreateVersion7(), supplier.Id, DeliveryCity: "Landshut",
            Lines: [new SaveOrderLineRequest(Guid.CreateVersion7(), material.Id, material.Name, 12, "t", 89.5m)]), Ct);
        var loaded = await client.GetOrderAsync(order.Id, Ct);
        var company = await client.GetCompanyAsync(Ct);
        var inquiry = await client.SaveCallAsync(new SaveCallRequest(Guid.CreateVersion7(), CallKind.Inquiry, CallerName: "Herr Bauer",
            CallerPhone: "0871 123", Subject: "Estrich für EFH", AreaSquareMeters: 140, SiteCity: "Altdorf"), Ct);
        var converted = await client.ConvertInquiryAsync(inquiry.Id, "EFH Bauer", cancellationToken: Ct);

        updated.Phone.ShouldBe("0871 999");
        dryRun.DryRun.ShouldBeTrue();
        dryRun.WouldImport.ShouldBe(1);
        material.SupplierName.ShouldBe("Baustoff Huber KG");
        loaded.Total.ShouldBe(1074m);
        Reports.OrderSheet(loaded, company, DateTimeOffset.UtcNow).ShouldContain("CT-C25-F4");
        Reports.OrderText(loaded, company).ShouldContain("12 t  CT-C25-F4");
        converted.Inquiry.InquiryStatus.ShouldBe(InquiryStatus.Converted);
        (await client.GetBauakteAsync(converted.BauakteId, Ct)).Title.ShouldBe("EFH Bauer");
        (await client.GetInquiriesAsync(InquiryStatus.Converted, cancellationToken: Ct)).ShouldHaveSingleItem();
        await client.DeleteOrderAsync(order.Id, Ct);
        await client.DeleteMaterialAsync(material.Id, Ct);
        await client.DeleteSupplierAsync(supplier.Id, Ct);
        (await client.GetSuppliersAsync(cancellationToken: Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Bauakte_DeleteNeedsOwnPassword_NotesCanBePinnedAndColored()
    {
        using var client = await SignedInAsync("buero", StandardRoles.OfficeId);
        var bauakte = await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH Kraus"), Ct);
        var note = await client.AddNoteAsync(bauakte.Id, "Schlüssel beim Nachbarn", pinned: true, Ct);
        var colored = await client.UpdateNoteAsync(bauakte.Id, note.Id, color: "green", cancellationToken: Ct);
        var work = await client.CreateWorkItemAsync(new CreateWorkItemRequest(Guid.CreateVersion7(), bauakte.Id, WorkItemKind.Task, "Aufmaß"), Ct);
        var changed = await client.UpdateWorkItemAsync(work.Id, dueDate: new DateOnly(2026, 11, 2), assigneeUserId: client.User!.Id, cancellationToken: Ct);

        var wrong = await Should.ThrowAsync<ApiException>(() => client.DeleteBauakteAsync(bauakte.Id, "falsch-falsch", Ct));
        await client.DeleteBauakteAsync(bauakte.Id, AuthClient.DefaultPassword, Ct);

        note.IsPinned.ShouldBeTrue();
        colored.Color.ShouldBe("green");
        changed.AssigneeUserId.ShouldBe(client.User!.Id);
        changed.DueDate.ShouldBe(new DateOnly(2026, 11, 2));
        wrong.Status.ShouldBe(HttpStatusCode.Forbidden);
        (await Should.ThrowAsync<ApiException>(() => client.GetBauakteAsync(bauakte.Id, Ct))).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Administration_UsersRolesDevicesBackupAuditCompany()
    {
        await CompleteSetupAsync();
        using var admin = new EstrichOneClient(new Uri("http://localhost"), new MemoryTokenStore(), _factory.Server.CreateHandler());
        await admin.SignInAsync("admin", AuthClient.DefaultPassword, Ct);

        var created = await admin.CreateUserAsync(new CreateUserRequest("Sepp Maier", "s.maier", null, "0171 1", [StandardRoles.SiteWorkerId]), Ct);
        var edited = await admin.UpdateUserAsync(created.User.Id, new UpdateUserRequest("Josef Maier", "s.maier@example.org", null), Ct);
        await admin.SetUserRolesAsync(created.User.Id, [StandardRoles.SiteWorkerId, StandardRoles.OfficeId], Ct);
        var reset = await admin.ResetPasswordAsync(created.User.Id, Ct);
        var locked = await admin.SetUserActiveAsync(created.User.Id, false, Ct);
        var unlocked = await admin.SetUserActiveAsync(created.User.Id, true, Ct);
        var roles = await admin.GetRolesAsync(Ct);
        var catalog = await admin.GetPermissionCatalogAsync(Ct);
        var code = await admin.CreateEnrollmentCodeAsync(DeviceKind.Pool, "Fahrzeug 3", Ct);
        var backup = await admin.GetBackupStatusAsync(Ct);
        var company = await admin.SaveCompanyAsync(new SaveCompanyRequest("Estrichbau Huber GmbH", "Industriestr. 5", "84030", "Ergolding", "0871 5", null, null), Ct);
        var audit = await admin.GetAuditAsync(new AuditQuery(Action: "users."), Ct);

        created.OneTimePassword.ShouldNotBeNullOrWhiteSpace();
        reset.OneTimePassword.ShouldNotBe(created.OneTimePassword);
        edited.DisplayName.ShouldBe("Josef Maier");
        locked.IsActive.ShouldBeFalse();
        unlocked.IsActive.ShouldBeTrue();
        (await admin.GetUsersAsync(Ct)).Single(u => u.UserName == "s.maier").RoleIds.Count.ShouldBe(2);
        roles.ShouldContain(r => r.Name == "Administrator" && r.IsSystem);
        catalog.ShouldContain(p => p.Code == "bauakten.read");
        code.Code.ShouldNotBeNullOrWhiteSpace();
        code.Kind.ShouldBe(DeviceKind.Pool);
        (await admin.GetDevicesAsync(Ct)).ShouldBeEmpty();
        backup.Summary.ShouldNotBeNullOrWhiteSpace();
        company.Name.ShouldBe("Estrichbau Huber GmbH");
        audit.Items.ShouldContain(i => i.Action == "users.created");
        Labels.AuditAction("users.created").ShouldBe("Benutzer angelegt");

        // Einmalpasswort: Anmeldung verlangt eigenes Passwort, die Desktop-App ändert es nativ.
        using var maier = new EstrichOneClient(new Uri("http://localhost"), new MemoryTokenStore(), _factory.Server.CreateHandler());
        await maier.SignInAsync("s.maier", reset.OneTimePassword, Ct);
        maier.MustChangePassword.ShouldBeTrue();
        await maier.ChangePasswordAsync(reset.OneTimePassword, "Neues-Passwort-2026!", Ct);
        maier.MustChangePassword.ShouldBeFalse();
    }

    [Fact]
    public async Task Helpdesk_AgentAnswersAssignsAndResolves_ReporterReopens()
    {
        using var reporter = await SignedInAsync("baustelle", StandardRoles.SiteWorkerId);
        using var agent = await SignedInAsync("it", StandardRoles.ItAdministratorId);
        var ticket = await reporter.ReportProblemAsync(new CreateTicketRequest(Guid.CreateVersion7(), "Tablet startet nicht", null, TicketCategory.DeviceDefect,
            TicketSource.Desktop, true), Ct);

        var assigned = await agent.UpdateTicketAsync(ticket.Summary.Id, new UpdateTicketRequest(Status: TicketStatus.InProgress, Priority: TicketPriority.High,
            AssigneeUserId: agent.User!.Id), Ct);
        await agent.CommentTicketAsync(ticket.Summary.Id, "Interne Notiz: Ersatzgerät vorbereiten", true, Ct);
        await agent.CommentTicketAsync(ticket.Summary.Id, "Bitte das Tablet heute abgeben.", false, Ct);
        await agent.UpdateTicketAsync(ticket.Summary.Id, new UpdateTicketRequest(Status: TicketStatus.Resolved, Resolution: "Gerät getauscht"), Ct);
        var seenByReporter = await reporter.GetTicketAsync(ticket.Summary.Id, Ct);
        var reopened = await reporter.ReopenTicketAsync(ticket.Summary.Id, Ct);

        assigned.Summary.AssigneeName.ShouldNotBeNull();
        assigned.Summary.Priority.ShouldBe(TicketPriority.High);
        seenByReporter.Comments.ShouldHaveSingleItem().Text.ShouldBe("Bitte das Tablet heute abgeben.");
        seenByReporter.Resolution.ShouldBe("Gerät getauscht");
        reopened.Summary.Status.ShouldNotBe(TicketStatus.Resolved);
        (await agent.GetTicketsAsync("all", TicketCategory.DeviceDefect, Ct)).ShouldHaveSingleItem();
        (await agent.GetTicketAsync(ticket.Summary.Id, Ct)).Comments.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Reports_SiteReportAndAgenda_ContainTheData()
    {
        await CompleteSetupAsync();
        using var client = await SignedInAsync("buero", StandardRoles.OfficeId);
        var bauakte = await client.CreateBauakteAsync(new CreateBauakteRequest(Guid.CreateVersion7(), "EFH <Kraus>", "Ahornweg 3", "84028", "Landshut"), Ct);
        await client.AddNoteAsync(bauakte.Id, "Zufahrt über Feldweg", pinned: true, Ct);
        var people = (await client.GetDirectoryAsync(Ct)).ToDictionary(p => p.Id, p => p.DisplayName);
        var data = new SiteReportData(bauakte, await client.GetCompanyAsync(Ct), null, "Max Huber", await client.GetMembersAsync(bauakte.Id, Ct),
            await client.GetRoomsAsync(bauakte.Id, Ct), [], await client.GetNotesAsync(bauakte.Id, Ct), [], [], [],
            [new ReportPhoto(Reports.DataUri(Png, "image/png"), "Foto 1")], people, DateTimeOffset.UtcNow);

        var html = Reports.SiteReport(data, new SiteReportOptions());
        var agenda = Reports.Agenda("Arbeitsplan", null, [new DateOnly(2026, 10, 19)], [], [], DateTimeOffset.UtcNow);

        html.ShouldContain("EFH &lt;Kraus&gt;");
        html.ShouldContain("Zufahrt über Feldweg");
        html.ShouldContain("data:image/png;base64,");
        html.ShouldContain("Ahornweg 3, 84028 Landshut");
        Reports.SiteReport(data, new SiteReportOptions(Photos: false, Notes: false)).ShouldNotContain("Zufahrt über Feldweg");
        agenda.ShouldContain("Keine Einsätze.");
        Palette.Of(BauakteStatus.Completed).ShouldBe(Palette.Success);
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
