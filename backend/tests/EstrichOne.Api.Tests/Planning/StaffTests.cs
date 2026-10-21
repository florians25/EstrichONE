using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Planning;

/// <summary>Personalstamm (C5): Stammdaten, Benutzerkonto, Kolonnen, Qualifikationen mit Ablauf, Urlaub, Geräte, Personaldokumente, Rechte.</summary>
public sealed class StaffTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] Weekdays = ["monday", "tuesday", "wednesday", "thursday", "friday"];

    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_factory.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);

    private static async Task<T> PutAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PutAsJsonAsync(url, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string name)
    {
        var content = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n" + name);
        var id = Guid.CreateVersion7();
        using var announce = await client.PostAsJsonAsync("/api/v1/files", new
        {
            id, kind = "document", originalName = name, contentType = "application/pdf",
            sizeBytes = (long)content.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
        }, Ct);
        announce.StatusCode.ShouldBe(HttpStatusCode.Created, await announce.Content.ReadAsStringAsync(Ct));
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new("application/octet-stream");
        using var upload = await client.PutAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), body, Ct);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        return id;
    }

    [Fact]
    public async Task Employee_FullRecord_LinkedToUser_CrewsQualificationsVacationEquipmentDocuments()
    {
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var lukas = await _factory.CreateUserAsync("lukas", roleIds: StandardRoles.SiteWorkerId);
        var crewId = Guid.CreateVersion7();
        await PutAsync<CrewResponse>(admin, $"/api/v1/planning/crews/{crewId}", new { name = "Kolonne Nord" });
        var truck = Guid.CreateVersion7();
        await PutAsync<ResourceResponse>(admin, $"/api/v1/planning/resources/{truck}", new { kind = "vehicle", name = "Sprinter 1", identifier = "LA-EO 1" });

        var id = Guid.CreateVersion7();
        var year = Today.Year;
        var saved = await PutAsync<EmployeeDetail>(admin, $"/api/v1/staff/employees/{id}", new
        {
            firstName = "Lukas", lastName = "Berger", personnelNumber = "P-017", jobTitle = "Estrichleger", employmentType = "fullTime", userId = lukas.Id,
            mobile = "0171 1234567", street = "Hauptstr. 4", postalCode = "84028", city = "Landshut", birthDate = "1990-04-12",
            emergencyContactName = "Maria Berger", emergencyContactPhone = "0871 55555", emergencyContactRelation = "Ehefrau",
            entryDate = "2019-03-01", workTimeModel = "Vollzeit 40 h", weeklyHours = 40m, workDays = Weekdays,
            hourlyRate = 21.5m, vacationDaysPerYear = 28m, vacationCarryOver = 2m,
        });
        saved.DisplayName.ShouldBe("Lukas Berger");
        saved.UserName.ShouldBe("lukas");
        saved.HourlyRate.ShouldBe(21.5m);
        saved.CanManage.ShouldBeTrue();

        // Kolonne, Qualifikationen (eine läuft bald ab), Urlaub (5 Arbeitstage), Fahrzeug, Dokument.
        var crews = await PutAsync<List<CrewRef>>(admin, $"/api/v1/staff/employees/{id}/crews", new { crewIds = new[] { crewId } });
        crews.ShouldHaveSingleItem().Name.ShouldBe("Kolonne Nord");
        await PutAsync<QualificationView>(admin, $"/api/v1/staff/employees/{id}/qualifications/{Guid.CreateVersion7()}", new
        {
            kind = "driverLicense", name = "C1E", issuedOn = "2015-05-01", expiresOn = Today.AddDays(20).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        });
        var forklift = await PutAsync<QualificationView>(admin, $"/api/v1/staff/employees/{id}/qualifications/{Guid.CreateVersion7()}", new
        {
            kind = "certificate", name = "Staplerschein", number = "ST-2020-11",
        });
        forklift.Expired.ShouldBeFalse();
        var monday = new DateOnly(year, 12, 1);
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(1);
        }

        await PutAsync<AbsenceView>(admin, $"/api/v1/staff/employees/{id}/absences/{Guid.CreateVersion7()}", new
        {
            from = monday, to = monday.AddDays(6), kind = "vacation", note = "Weihnachtsurlaub",
        });
        await PutAsync<EquipmentView>(admin, $"/api/v1/staff/employees/{id}/equipment/{Guid.CreateVersion7()}", new { resourceId = truck, issuedOn = "2024-01-15" });
        var file = await UploadAsync(admin, "Arbeitsvertrag.pdf");
        await PutAsync<EmployeeDocumentView>(admin, $"/api/v1/staff/employees/{id}/documents/{Guid.CreateVersion7()}", new
        {
            fileId = file, category = "contract", title = "Arbeitsvertrag",
        });

        var detail = (await admin.GetFromJsonAsync<EmployeeDetail>($"/api/v1/staff/employees/{id}", TestJson.Options, Ct))!;
        detail.Crews.ShouldHaveSingleItem().Id.ShouldBe(crewId);
        detail.Qualifications.Count.ShouldBe(2);
        detail.Qualifications.Single(q => q.Name == "C1E").ExpiresSoon.ShouldBeTrue();
        detail.Equipment.ShouldHaveSingleItem().Name.ShouldBe("Sprinter 1");
        detail.Equipment[0].SerialNumber.ShouldBe("LA-EO 1");
        detail.Documents.ShouldHaveSingleItem().FileName.ShouldBe("Arbeitsvertrag.pdf");
        detail.Absences.ShouldHaveSingleItem().Kind.ShouldBe(AbsenceKind.Vacation);
        if (Today < monday)
        {
            detail.Vacation.Planned.ShouldBe(5m, "Mo–Fr zählen, Wochenende nicht.");
            detail.Vacation.Remaining.ShouldBe(25m);
        }

        detail.WorkDays.Count.ShouldBe(5);

        // Die Planung kennt die Kolonnenzugehörigkeit und den Urlaub.
        var crew = (await admin.GetFromJsonAsync<List<CrewResponse>>("/api/v1/planning/crews", TestJson.Options, Ct))!.Single();
        crew.MemberUserIds.ShouldBe([lukas.Id]);

        // Büro sieht den Personalstamm ohne Stundensatz und Dokumente; Baustelle gar nicht.
        var list = (await office.GetFromJsonAsync<List<EmployeeListItem>>("/api/v1/staff/employees", TestJson.Options, Ct))!;
        var item = list.ShouldHaveSingleItem();
        item.HourlyRate.ShouldBeNull();
        item.ExpiringQualifications.ShouldBe(1);
        item.Crews.ShouldHaveSingleItem();
        var officeDetail = (await office.GetFromJsonAsync<EmployeeDetail>($"/api/v1/staff/employees/{id}", TestJson.Options, Ct))!;
        officeDetail.Documents.ShouldBeEmpty();
        officeDetail.CanManage.ShouldBeFalse();
        (await office.GetAsync(new Uri($"/api/v1/files/{file}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var officeSave = await office.PutAsJsonAsync($"/api/v1/staff/employees/{id}", new { lastName = "X" }, Ct);
        officeSave.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var worker = _factory.CreateClient();
        worker.WithToken((await worker.LoginOkAsync("lukas")).AccessToken);
        (await worker.GetAsync(new Uri("/api/v1/staff/employees", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Ein zweiter Administrator darf das Personaldokument öffnen (Freigabe über den Personalstamm).
        using var second = await _factory.CreateClientAsAsync("personal", StandardRoles.AdministratorId);
        (await second.GetAsync(new Uri($"/api/v1/files/{file}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var expiring = (await office.GetFromJsonAsync<List<ExpiringQualification>>("/api/v1/staff/qualifications/expiring", TestJson.Options, Ct))!;
        expiring.ShouldHaveSingleItem().Qualification.Name.ShouldBe("C1E");
    }

    [Fact]
    public async Task Validation_DuplicateUserLink_NumberTaken_ImportFromUsers_ExitedEmployees()
    {
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        var lukas = await _factory.CreateUserAsync("lukas", roleIds: StandardRoles.SiteWorkerId);
        await PutAsync<EmployeeDetail>(admin, $"/api/v1/staff/employees/{Guid.CreateVersion7()}", new { lastName = "Berger", personnelNumber = "P-1", userId = lukas.Id });

        using var twice = await admin.PutAsJsonAsync($"/api/v1/staff/employees/{Guid.CreateVersion7()}", new { lastName = "Zweiter", userId = lukas.Id }, Ct);
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var number = await admin.PutAsJsonAsync($"/api/v1/staff/employees/{Guid.CreateVersion7()}", new { lastName = "Dritter", personnelNumber = "P-1" }, Ct);
        number.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var exit = await admin.PutAsJsonAsync($"/api/v1/staff/employees/{Guid.CreateVersion7()}", new
        {
            lastName = "Falsch", entryDate = "2024-05-01", exitDate = "2024-04-01",
        }, Ct);
        exit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var unlinked = Guid.CreateVersion7();
        await PutAsync<EmployeeDetail>(admin, $"/api/v1/staff/employees/{unlinked}", new { firstName = "Paul", lastName = "Aushilfe", exitDate = "2024-12-31" });
        using var crewsWithoutAccount = await admin.PutAsJsonAsync($"/api/v1/staff/employees/{unlinked}/crews", new { crewIds = Array.Empty<Guid>() }, Ct);
        crewsWithoutAccount.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using var import = await admin.PostAsync(new Uri("/api/v1/staff/employees/import-users", UriKind.Relative), null, Ct);
        (await import.Content.ReadFromJsonAsync<ImportEmployeesResult>(TestJson.Options, Ct))!.Created.ShouldBe(1, "Nur „chefin“ hat noch keinen Personalstamm.");
        var active = (await admin.GetFromJsonAsync<List<EmployeeListItem>>("/api/v1/staff/employees", TestJson.Options, Ct))!;
        active.Select(e => e.LastName).ShouldBe(["Berger", "Huber"]);
        var former = (await admin.GetFromJsonAsync<List<EmployeeListItem>>("/api/v1/staff/employees?status=former", TestJson.Options, Ct))!;
        former.ShouldHaveSingleItem().DisplayName.ShouldBe("Paul Aushilfe");
    }
}
