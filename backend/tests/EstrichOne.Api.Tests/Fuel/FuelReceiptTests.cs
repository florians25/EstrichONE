using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Modules.Fuel;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Fuel;

/// <summary>
/// Tankbelege: Erfassen offline aus der App (Foto + Outbox), Nummernkreis, Vorbelegung mit dem Dispo-Fahrzeug,
/// Plausibilitätshinweise, Prüfen/Freigeben/Erstatten, Rechte, Auswertung, CSV für die Buchhaltung und Modulschalter.
/// </summary>
public sealed class FuelReceiptTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly byte[] Jpeg = CreateJpeg();
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_factory.Clock.GetUtcNow(), Berlin).DateTime);

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static byte[] CreateJpeg()
    {
        using var image = (NetVips.Image.Black(600, 900, bands: 3) + new[] { 230.0, 230.0, 225.0 }).Cast(NetVips.Enums.BandFormat.Uchar);
        return image.JpegsaveBuffer(q: 78);
    }

    private static async Task<T> PutAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PutAsJsonAsync(path, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private static async Task<Guid> MeAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/v1/auth/me", TestJson.Options, Ct))!["id"]!.GetValue<Guid>();

    private static async Task<Guid> VehicleAsync(HttpClient office, string name, string plate, decimal? tank = null)
    {
        var id = Guid.CreateVersion7();
        await PutAsync<ResourceResponse>(office, $"/api/v1/planning/resources/{id}", new { kind = "vehicle", name, identifier = plate, tankCapacityLiters = tank });
        return id;
    }

    private static object Receipt(Guid vehicleId, DateOnly date, int? km, decimal liters, decimal amount, string payment = "fuelCard", string fuelType = "diesel",
        Guid? imageFileId = null, string? number = null) =>
        new { vehicleId, date, odometerKm = km, liters, amountGross = amount, fuelType, paymentMethod = payment, imageFileId, number };

    private static async Task<FuelReceiptResponse> SaveAsync(HttpClient client, object body) =>
        await PutAsync<FuelReceiptResponse>(client, $"/api/v1/fuel-receipts/{Guid.CreateVersion7()}", body);

    [Fact]
    public async Task OfflineReceiptFromTheApp_IsNumbered_PrefilledWithTodaysVehicle_AndThePhotoIsArchivedAsReceipt()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var worker = await _factory.CreateClientAsAsync("lukas", StandardRoles.SiteWorkerId);
        var lukas = await MeAsync(worker);
        var sprinter = await VehicleAsync(office, "Sprinter 3", "LA-EB 300", tank: 80);
        await VehicleAsync(office, "Pritsche 1", "LA-EB 100");

        // Heute eingeplant mit dem Sprinter: Die App schlägt ihn vor.
        using var created = await office.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "Halle Nord", city = "Landshut" }, Ct);
        var bauakte = (await created.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
        await PutAsync<AssignmentView>(office, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = bauakte.Id, day = Today, startTime = "07:00:00", activity = "Estrich einbringen", userIds = new[] { lukas }, resourceIds = new[] { sprinter },
        });
        var options = (await worker.GetFromJsonAsync<FuelOptionsResponse>("/api/v1/fuel-receipts/options", TestJson.Options, Ct))!;
        options.SuggestedVehicleId.ShouldBe(sprinter);
        options.Vehicles.Select(v => v.Name).ShouldBe(["Pritsche 1", "Sprinter 3"]);
        options.Vehicles.Single(v => v.Id == sprinter).TankCapacityLiters.ShouldBe(80m);

        var file = Guid.CreateVersion7();
        var receiptId = Guid.CreateVersion7();
        var announce = SyncClient.Op("file.announce", file, new
        {
            kind = "photo", originalName = "Tankbeleg.jpg", contentType = "image/jpeg", sizeBytes = Jpeg.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(Jpeg)),
        });
        var create = SyncClient.Op("fuelReceipt.create", receiptId, new
        {
            vehicleId = sprinter, date = Today, odometerKm = 48_210, liters = 61.37m, amountGross = 104.27m, fuelType = "diesel", paymentMethod = "private",
            station = "Aral Landshut Nord", imageFileId = file, source = "ios",
        });
        var first = await worker.PushAsync(announce, create);
        var again = await worker.PushAsync(create);
        first.Results.ShouldAllBe(r => r.Status == SyncOperationStatus.Applied);
        again.Results.ShouldHaveSingleItem().Status.ShouldBe(SyncOperationStatus.Duplicate);
        first.Results[1].Entity!["number"]!.GetValue<string>().ShouldBe($"TB-{Today.Year}-0001");

        using var body = new ByteArrayContent(Jpeg);
        body.Headers.ContentType = new("application/octet-stream");
        (await worker.PutAsync(new Uri($"/api/v1/files/{file}/content", UriKind.Relative), body, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var mine = (await worker.GetFromJsonAsync<List<FuelReceiptResponse>>("/api/v1/fuel-receipts/mine", TestJson.Options, Ct))!;
        var receipt = mine.ShouldHaveSingleItem();
        receipt.Number.ShouldBe($"TB-{Today.Year}-0001");
        receipt.VehicleName.ShouldBe("Sprinter 3");
        receipt.Source.ShouldBe("ios");
        receipt.PricePerLiter.ShouldBe(1.699m);
        receipt.ImageStored.ShouldBeTrue();
        receipt.StatusText.ShouldBe("Neu");
        receipt.CanEdit.ShouldBeTrue();

        // Büro sieht das Foto (Recht fuel.manage); der Beleg gehört zur Archivregel „Tankbelege“ (10 Jahre).
        (await office.GetAsync(new Uri($"/api/v1/files/{file}/content", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        (await db.Set<FileRecord>().SingleAsync(f => f.Id == file, Ct)).Category.ShouldBe("fuelReceipt");
        EstrichOne.Api.Modules.Archive.ArchiveService.DefaultRules.ShouldContain(r => r.DocumentType == "fuelReceipt" && r.RetentionYears == 10 && r.Enabled);
    }

    [Fact]
    public async Task Plausibility_WarnsAboutOdometerBackwards_Consumption_Duplicates_AndTankSize()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var sprinter = await VehicleAsync(office, "Sprinter 3", "LA-EB 300", tank: 80);
        var day = Today.AddDays(-20);
        await SaveAsync(office, Receipt(sprinter, day, 10_000, 60, 100));
        var normal = await SaveAsync(office, Receipt(sprinter, day.AddDays(5), 10_600, 66, 110));
        await SaveAsync(office, Receipt(sprinter, day.AddDays(10), 11_200, 63, 105));
        await SaveAsync(office, Receipt(sprinter, day.AddDays(12), 11_800, 64.5m, 107.5m));
        var thirsty = await SaveAsync(office, Receipt(sprinter, day.AddDays(14), 11_900, 70, 117));
        var backwards = await SaveAsync(office, Receipt(sprinter, day.AddDays(16), 11_500, 20, 33.4m));
        var tooMuch = await SaveAsync(office, Receipt(sprinter, day.AddDays(18), 12_500, 95, 160));
        var duplicate = await SaveAsync(office, Receipt(sprinter, day.AddDays(18), 12_501, 1, 160, fuelType: "adBlue"));

        var list = (await office.GetFromJsonAsync<List<FuelReceiptResponse>>($"/api/v1/fuel-receipts?vehicleId={sprinter}", TestJson.Options, Ct))!
            .ToDictionary(r => r.Id);
        list[normal.Id].DistanceKm.ShouldBe(600);
        list[normal.Id].ConsumptionPer100Km.ShouldBe(11.0m);
        list[normal.Id].Warnings.Select(w => w.Code).ShouldBe(["missing_image"]);
        list[thirsty.Id].ConsumptionPer100Km.ShouldBe(70.0m);
        list[thirsty.Id].Warnings.ShouldContain(w => w.Code == "consumption_unusual");
        list[backwards.Id].Warnings.ShouldContain(w => w.Code == "odometer_backwards" && w.Text.Contains("11.900 km", StringComparison.Ordinal));
        list[tooMuch.Id].Warnings.ShouldContain(w => w.Code == "exceeds_tank");
        list[tooMuch.Id].Warnings.ShouldContain(w => w.Code == "duplicate");
        list[duplicate.Id].Warnings.ShouldContain(w => w.Code == "duplicate" && w.Text.Contains(tooMuch.Number, StringComparison.Ordinal));
        list[duplicate.Id].Warnings.ShouldNotContain(w => w.Code == "exceeds_tank"); // AdBlue zählt nicht zum Tank

        var flagged = (await office.GetFromJsonAsync<List<FuelReceiptResponse>>("/api/v1/fuel-receipts?warningsOnly=true", TestJson.Options, Ct))!;
        flagged.ShouldNotContain(r => r.Id == normal.Id);

        var statistics = (await office.GetFromJsonAsync<List<FuelVehicleStatistics>>($"/api/v1/fuel-receipts/statistics?from={day.AddDays(-1):yyyy-MM-dd}",
            TestJson.Options, Ct))!;
        var sprinterStats = statistics.ShouldHaveSingleItem();
        sprinterStats.Receipts.ShouldBe(8);
        sprinterStats.AdBlueLiters.ShouldBe(1m);
        sprinterStats.Cost.ShouldBe(100 + 110 + 105 + 107.5m + 117 + 33.4m + 160 + 160);
        sprinterStats.LastOdometerKm.ShouldBe(12_501);
        sprinterStats.Months.Sum(m => m.Receipts).ShouldBe(8);
        sprinterStats.AverageConsumption.ShouldNotBeNull();
    }

    [Fact]
    public async Task Review_ChecksRejectsAndSettles_WithRights_AndSettledReceiptsAreLocked()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var worker = await _factory.CreateClientAsAsync("lukas", StandardRoles.SiteWorkerId);
        using var colleague = await _factory.CreateClientAsAsync("anna", StandardRoles.SiteWorkerId);
        var sprinter = await VehicleAsync(office, "Sprinter 3", "LA-EB 300");
        var own = await SaveAsync(worker, Receipt(sprinter, Today, 1000, 40, 68.5m, payment: "private"));
        var card = await SaveAsync(worker, Receipt(sprinter, Today.AddDays(-1), 900, 30, 51));

        // Nummern nur aus dem Nummernkreis.
        using var manual = await worker.PutAsJsonAsync($"/api/v1/fuel-receipts/{Guid.CreateVersion7()}", Receipt(sprinter, Today, 1100, 10, 17, number: "TB-1"), Ct);
        manual.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Baustelle: nur eigene, keine Liste aller, keine Entscheidung, kein Export.
        (await worker.GetAsync(new Uri("/api/v1/fuel-receipts", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await worker.PostAsJsonAsync("/api/v1/fuel-receipts/decisions", new { ids = new[] { own.Id }, action = "check" }, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await worker.GetAsync(new Uri("/api/v1/fuel-receipts/export.csv", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await colleague.GetAsync(new Uri($"/api/v1/fuel-receipts/{own.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await colleague.GetFromJsonAsync<List<FuelReceiptResponse>>("/api/v1/fuel-receipts/mine", TestJson.Options, Ct))!.ShouldBeEmpty();

        // Ablehnen nur mit Grund; Korrektur legt den Beleg wieder vor.
        using var noReason = await office.PostAsJsonAsync("/api/v1/fuel-receipts/decisions", new { ids = new[] { own.Id }, action = "reject" }, Ct);
        noReason.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var rejected = await office.PostAsJsonAsync("/api/v1/fuel-receipts/decisions",
            new { ids = new[] { own.Id }, action = "reject", reason = "Betrag unleserlich" }, Ct);
        (await rejected.Content.ReadFromJsonAsync<List<FuelReceiptResponse>>(TestJson.Options, Ct))!.Single().Status.ShouldBe(FuelReceiptStatus.Rejected);
        var corrected = await PutAsync<FuelReceiptResponse>(worker, $"/api/v1/fuel-receipts/{own.Id}", Receipt(sprinter, Today, 1000, 40, 68.9m, payment: "private"));
        corrected.Status.ShouldBe(FuelReceiptStatus.New);
        corrected.AmountGross.ShouldBe(68.9m);

        // Freigeben (mehrere), danach darf die Baustelle nicht mehr ändern; Erstatten nur nach Freigabe.
        using var settleTooEarly = await office.PostAsJsonAsync("/api/v1/fuel-receipts/decisions", new { ids = new[] { own.Id }, action = "settle" }, Ct);
        settleTooEarly.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var check = await office.PostAsJsonAsync("/api/v1/fuel-receipts/decisions", new { ids = new[] { own.Id, card.Id }, action = "check" }, Ct);
        (await check.Content.ReadFromJsonAsync<List<FuelReceiptResponse>>(TestJson.Options, Ct))!.ShouldAllBe(r => r.Status == FuelReceiptStatus.Checked);
        (await worker.PutAsJsonAsync($"/api/v1/fuel-receipts/{own.Id}", Receipt(sprinter, Today, 1000, 40, 1, payment: "private"), Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);
        using var settle = await office.PostAsJsonAsync("/api/v1/fuel-receipts/decisions", new { ids = new[] { own.Id, card.Id }, action = "settle" }, Ct);
        var settled = (await settle.Content.ReadFromJsonAsync<List<FuelReceiptResponse>>(TestJson.Options, Ct))!.ToDictionary(r => r.Id);
        settled[own.Id].StatusText.ShouldBe("Erstattet");
        settled[card.Id].StatusText.ShouldBe("Gebucht");
        settled[own.Id].SettledAt.ShouldNotBeNull();

        (await office.PutAsJsonAsync($"/api/v1/fuel-receipts/{own.Id}", Receipt(sprinter, Today, 1000, 40, 1, payment: "private"), Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Conflict);
        (await office.DeleteAsync(new Uri($"/api/v1/fuel-receipts/{own.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var privateOnly = (await office.GetFromJsonAsync<List<FuelReceiptResponse>>("/api/v1/fuel-receipts?paymentMethod=private&status=settled",
            TestJson.Options, Ct))!;
        privateOnly.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
    }

    [Fact]
    public async Task Export_IsDatevFriendlyCsv_WithoutRejectedReceipts()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var sprinter = await VehicleAsync(office, "Sprinter 3", "LA-EB 300");
        var day = new DateOnly(Today.Year, Today.Month, 1);
        await SaveAsync(office, Receipt(sprinter, day, 1000, 61.37m, 104.27m, payment: "private"));
        var wrong = await SaveAsync(office, Receipt(sprinter, day, 1010, 5, 9));
        (await office.PostAsJsonAsync("/api/v1/fuel-receipts/decisions", new { ids = new[] { wrong.Id }, action = "reject", reason = "doppelt" }, Ct))
            .EnsureSuccessStatusCode();

        using var response = await office.GetAsync(new Uri($"/api/v1/fuel-receipts/export.csv?from={day:yyyy-MM-dd}&to={Today:yyyy-MM-dd}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(l => l.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToList();
        lines.Count.ShouldBe(2);
        lines[0].ShouldStartWith("Umsatz (brutto);Soll/Haben-Kennzeichen;WKZ Umsatz;Belegdatum;Belegfeld 1;Buchungstext;KOST1 - Kostenstelle");
        var fields = lines[1].Split(';');
        fields[0].ShouldBe("104,27");
        fields[1].ShouldBe("S");
        fields[3].ShouldBe(day.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture));
        fields[4].ShouldBe($"TB-{Today.Year}-0001");
        fields[5].ShouldBe("Diesel 61,37 l LA-EB 300");
        fields[6].ShouldStartWith("20");
        fields[12].ShouldBe("1,699");
        fields[14].ShouldBe("Privat ausgelegt");
        fields[15].ShouldBe("Max Huber");
    }

    [Fact]
    public async Task ModuleSwitchedOff_BlocksEndpoints_AndRejectsOfflineReceipts()
    {
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        using var worker = await _factory.CreateClientAsAsync("lukas", StandardRoles.SiteWorkerId);
        var sprinter = await VehicleAsync(admin, "Sprinter 3", "LA-EB 300");
        var modules = (await admin.GetFromJsonAsync<EstrichOne.Api.Modules.Organization.ModulesResponse>("/api/v1/settings/modules", TestJson.Options, Ct))!;
        var fuel = modules.Modules.Single(m => m.Key == "fuel");
        fuel.Requires.ShouldBe(["machines"]);
        fuel.Title.ShouldBe("Tankbelege");

        (await admin.PutAsJsonAsync("/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["fuel"] = false } }, Ct)).EnsureSuccessStatusCode();
        using var blocked = await worker.GetAsync(new Uri("/api/v1/fuel-receipts/mine", UriKind.Relative), Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var result = await worker.PushAsync(SyncClient.Op("fuelReceipt.create", Guid.CreateVersion7(), new
        {
            vehicleId = sprinter, date = Today, liters = 10, amountGross = 17, fuelType = "diesel", paymentMethod = "fuelCard",
        }));
        result.Results.ShouldHaveSingleItem().Code.ShouldBe("MODULE_DISABLED");

        // Maschinen aus → Tankbelege gehen mit aus.
        (await admin.PutAsJsonAsync("/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["fuel"] = true, ["machines"] = false } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public void Analysis_UsesVehicleMedian_OnceThereAreEnoughComparisons()
    {
        FuelAnalysis.IsUnusual(12, 11, 3).ShouldBeFalse();
        FuelAnalysis.IsUnusual(19, 11, 3).ShouldBeTrue();
        FuelAnalysis.IsUnusual(5, 11, 3).ShouldBeTrue();
        FuelAnalysis.IsUnusual(30, 11, 2).ShouldBeFalse(); // zu wenige Vergleichswerte: nur grobe Grenzen
        FuelAnalysis.IsUnusual(50, null, 0).ShouldBeTrue();
        FuelCsv.Field("=SUMME(A1)").ShouldBe("'=SUMME(A1)");
        FuelCsv.Field("Aral; Nord").ShouldBe("\"Aral; Nord\"");
        FuelCsv.Field("-12,50").ShouldBe("-12,50");
    }
}
