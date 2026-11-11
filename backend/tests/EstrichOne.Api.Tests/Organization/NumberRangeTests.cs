using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Calls;
using EstrichOne.Api.Modules.Materials;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Modules.Work;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Organization;

/// <summary>Nummernkreise aller Datensätze: Vergabe beim Anlegen, Parallelität, Erschöpfung, Handvergabe, Einstellungen, Migration.</summary>
public sealed class NumberRangeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;
    private HttpClient _office = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private int Year => TimeZoneInfo.ConvertTime(_factory.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).Year;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _admin = await _factory.CreateClientAsAsync("chef.nummern", StandardRoles.AdministratorId);
        _office = await _factory.CreateClientAsAsync("buero.nummern", StandardRoles.OfficeId);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _office.Dispose();
        await _factory.DisposeAsync();
    }

    private static async Task<T> PutAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PutAsJsonAsync(url, body, TestJson.Options, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body, TestJson.Options, Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task Settings_ListAllRanges_WithDefaultsAndPreview_OnlyForAdmins()
    {
        var ranges = (await _admin.GetFromJsonAsync<List<NumberRangeResponse>>("/api/v1/settings/number-ranges", TestJson.Options, Ct))!;
        ranges.Select(r => r.Key).ShouldBe(NumberRangeDefinitions.All.Select(d => d.Key));
        var customer = ranges.Single(r => r.Key == "customer");
        customer.Pattern.ShouldBe("60{NNNN}");
        customer.Preview.ShouldBe("600001");
        customer.RangeEnd.ShouldBe(9999);
        customer.Remaining.ShouldBe(9999);
        ranges.Single(r => r.Key == "supplier").Preview.ShouldBe("700001");
        ranges.Single(r => r.Key == "employee").Preview.ShouldBe("10001");
        var bauakte = ranges.Single(r => r.Key == "bauakte");
        bauakte.Pattern.ShouldBe("{JJJJ}-{NNN}");
        bauakte.Yearly.ShouldBeTrue();
        bauakte.RangeEnd.ShouldBeNull();
        bauakte.Remaining.ShouldBeNull();
        ranges.Single(r => r.Key == "order").Preview.ShouldBe($"B-{Year}-0001");

        (await _office.GetAsync(new Uri("/api/v1/settings/number-ranges", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _office.PutAsJsonAsync("/api/v1/settings/number-ranges/crew", new { pattern = "K{NNN}", rangeStart = 1 }, TestJson.Options, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MasterData_AndProcesses_AreNumberedOnCreate_UpdatesKeepTheNumber()
    {
        var supplierId = Guid.CreateVersion7();
        var supplier = await PutAsync<SupplierResponse>(_office, $"/api/v1/suppliers/{supplierId}", new { id = supplierId, name = "Baustoff Huber" });
        supplier.Number.ShouldBe("700001");
        (await PutAsync<SupplierResponse>(_office, $"/api/v1/suppliers/{supplierId}", new { id = supplierId, name = "Baustoff Huber KG" })).Number.ShouldBe("700001");

        var materialId = Guid.CreateVersion7();
        var material = await PutAsync<MaterialResponse>(_office, $"/api/v1/materials/{materialId}",
            new { id = materialId, name = "CT-C25-F4", category = "screed", unit = "t", articleNumber = "HZ-4711", supplierId });
        material.Number.ShouldBe("500001");
        material.ArticleNumber.ShouldBe("HZ-4711");
        (await _office.GetFromJsonAsync<List<MaterialResponse>>("/api/v1/materials?search=500001", TestJson.Options, Ct))!.ShouldHaveSingleItem();

        (await PutAsync<ResourceResponse>(_office, $"/api/v1/planning/resources/{Guid.CreateVersion7()}", new { kind = "vehicle", name = "Sprinter" }))
            .InventoryNumber.ShouldBe("20001");
        (await PutAsync<CrewResponse>(_office, $"/api/v1/planning/crews/{Guid.CreateVersion7()}", new { name = "Kolonne Nord" })).Number.ShouldBe("K01");
        (await PutAsync<EmployeeDetail>(_admin, $"/api/v1/staff/employees/{Guid.CreateVersion7()}", new { lastName = "Berger", employmentType = "fullTime" }))
            .PersonnelNumber.ShouldBe("10001");

        var inquiryId = Guid.CreateVersion7();
        (await PutAsync<CallResponse>(_office, $"/api/v1/calls/{inquiryId}", new { id = inquiryId, kind = "inquiry", callerName = "Familie Kraus" }))
            .Number.ShouldBe($"A-{Year}-0001");
        var callId = Guid.CreateVersion7();
        (await PutAsync<CallResponse>(_office, $"/api/v1/calls/{callId}", new { id = callId, kind = "call", callerName = "Herr Maier" })).Number.ShouldBeNull();
        (await _office.GetFromJsonAsync<List<CallResponse>>($"/api/v1/calls?search=A-{Year}-0001", TestJson.Options, Ct))!.ShouldHaveSingleItem();

        (await PostAsync<WorkItemDto>(_office, "/api/v1/work-items", new { id = Guid.CreateVersion7(), kind = "task", title = "Angebot" })).Number.ShouldBe($"AU-{Year}-0001");
        (await PostAsync<WorkItemDto>(_office, "/api/v1/work-items", new { id = Guid.CreateVersion7(), kind = "defect", title = "Riss" })).Number.ShouldBe($"M-{Year}-0001");
    }

    [Fact]
    public async Task AppSync_GetsServerNumbers_AndExhaustionRejectsOnlyThatOperation()
    {
        var task = Guid.CreateVersion7();
        var customer = Guid.CreateVersion7();
        var results = (await _office.PushAsync(
            Sync.SyncClient.Op("workitem.create", task, new { kind = "defect", title = "Riss in der Garage" }),
            Sync.SyncClient.Op("contact.create", customer, new { name = "Kunde aus der App" }))).Results;
        results.Select(r => r.Status).ShouldAllBe(status => status == Modules.Sync.Exports.SyncOperationStatus.Applied);
        results[0].Entity!["number"]!.GetValue<string>().ShouldBe($"M-{Year}-0001");
        results[1].Entity!["customerNumber"]!.GetValue<string>().ShouldBe("600001");

        await PutAsync<NumberRangeResponse>(_admin, "/api/v1/settings/number-ranges/task", new { pattern = "AU-{JJJJ}-{N}", rangeStart = 1, rangeEnd = 1, nextNumber = 1 });
        await PostAsync<WorkItemDto>(_office, "/api/v1/work-items", new { id = Guid.CreateVersion7(), kind = "task", title = "Letzte Aufgabe" });
        var rejected = (await _office.PushAsync(Sync.SyncClient.Op("workitem.create", Guid.CreateVersion7(), new { kind = "task", title = "Zu viel" }))).Results.Single();
        rejected.Status.ShouldBe(Modules.Sync.Exports.SyncOperationStatus.Rejected);
        rejected.Code.ShouldBe("NUMBER_RANGE_EXHAUSTED");
    }

    [Fact]
    public async Task ManualNumbers_OnlyForAdmins_AndUnique()
    {
        var id = Guid.CreateVersion7();
        using (var forbidden = await _office.PutAsJsonAsync($"/api/v1/suppliers/{id}", new { id, name = "Eigene Nummer", number = "L-1" }, TestJson.Options, Ct))
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await forbidden.Content.ReadAsStringAsync(Ct)).ShouldContain("NUMBER_OVERRIDE_FORBIDDEN");
        }

        (await PutAsync<SupplierResponse>(_admin, $"/api/v1/suppliers/{id}", new { id, name = "Eigene Nummer", number = "700002" })).Number.ShouldBe("700002");
        // Sachbearbeiter sendet die vorhandene Nummer unverändert zurück: erlaubt.
        (await PutAsync<SupplierResponse>(_office, $"/api/v1/suppliers/{id}", new { id, name = "Eigene Nummer GmbH", number = "700002" })).Number.ShouldBe("700002");

        var other = Guid.CreateVersion7();
        using (var taken = await _admin.PutAsJsonAsync($"/api/v1/suppliers/{other}", new { id = other, name = "Doppelt", number = "700002" }, TestJson.Options, Ct))
        {
            taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task Counter_SkipsManualNumbers()
    {
        var manual = Guid.CreateVersion7();
        await PutAsync<SupplierResponse>(_admin, $"/api/v1/suppliers/{manual}", new { id = manual, name = "Von Hand", number = "700001" });
        var next = Guid.CreateVersion7();
        (await PutAsync<SupplierResponse>(_office, $"/api/v1/suppliers/{next}", new { id = next, name = "Automatisch" })).Number.ShouldBe("700002");
    }

    [Fact]
    public async Task ParallelCreation_GetsDistinctNumbers()
    {
        var numbers = await Task.WhenAll(Enumerable.Range(1, 20).Select(i =>
            PutAsync<ResourceResponse>(_office, $"/api/v1/planning/resources/{Guid.CreateVersion7()}", new { kind = "machine", name = $"Maschine {i}" })));
        numbers.Select(r => r.InventoryNumber).Distinct().Count().ShouldBe(20);
        numbers.Select(r => int.Parse(r.InventoryNumber!, System.Globalization.CultureInfo.InvariantCulture)).Order().ShouldBe(Enumerable.Range(20001, 20));
    }

    [Fact]
    public async Task ExhaustedRange_Returns409_AndSettingsShowZeroRemaining()
    {
        var saved = await PutAsync<NumberRangeResponse>(_admin, "/api/v1/settings/number-ranges/crew", new { pattern = "KO-{NN}", rangeStart = 1, rangeEnd = 2, nextNumber = 2 });
        saved.Preview.ShouldBe("KO-02");
        saved.Remaining.ShouldBe(1);
        (await PutAsync<CrewResponse>(_office, $"/api/v1/planning/crews/{Guid.CreateVersion7()}", new { name = "Letzte" })).Number.ShouldBe("KO-02");

        using var response = await _office.PutAsJsonAsync($"/api/v1/planning/crews/{Guid.CreateVersion7()}", new { name = "Zu viel" }, TestJson.Options, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("NUMBER_RANGE_EXHAUSTED");
        var crew = (await _admin.GetFromJsonAsync<NumberRangeResponse>("/api/v1/settings/number-ranges/crew", TestJson.Options, Ct))!;
        crew.Remaining.ShouldBe(0);
    }

    [Fact]
    public async Task SaveRange_ValidatesPatternAndRange_ChecksVersion_AndIsAudited()
    {
        foreach (var invalid in new object[]
                 {
                     new { pattern = "K-", rangeStart = 1 },
                     new { pattern = "K{NN}{NN}", rangeStart = 1 },
                     new { pattern = "K<{NN}>", rangeStart = 1 },
                     new { pattern = "K{NN}", rangeStart = 1, rangeEnd = 500 },
                     new { pattern = "K{NN}", rangeStart = 10, rangeEnd = 5 },
                     new { pattern = "K{NN}", rangeStart = 10, rangeEnd = 20, nextNumber = 30 },
                 })
        {
            (await _admin.PutAsJsonAsync("/api/v1/settings/number-ranges/crew", invalid, TestJson.Options, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        (await _admin.PutAsJsonAsync("/api/v1/settings/number-ranges/unbekannt", new { pattern = "{NN}" }, TestJson.Options, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var current = (await _admin.GetFromJsonAsync<NumberRangeResponse>("/api/v1/settings/number-ranges/supplier", TestJson.Options, Ct))!;
        var saved = await PutAsync<NumberRangeResponse>(_admin, "/api/v1/settings/number-ranges/supplier",
            new { pattern = "LF{JJ}-{NNN}", rangeStart = 100, version = current.Version });
        saved.Yearly.ShouldBeTrue();
        saved.Preview.ShouldBe($"LF{Year % 100:00}-100");
        (await _admin.PutAsJsonAsync("/api/v1/settings/number-ranges/supplier", new { pattern = "70{NNNN}", rangeStart = 1, version = current.Version }, TestJson.Options, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        var id = Guid.CreateVersion7();
        (await PutAsync<SupplierResponse>(_office, $"/api/v1/suppliers/{id}", new { id, name = "Neu" })).Number.ShouldBe($"LF{Year % 100:00}-100");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM audit_events WHERE action = 'settings.number_range.saved' AND entity_id = 'supplier'")
            .SingleAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task LegacySettingsPatterns_StayInSyncWithRanges()
    {
        await PutAsync<NumberRangeResponse>(_admin, "/api/v1/settings/number-ranges/bauakte", new { pattern = "BA-{JJ}/{NNNN}", rangeStart = 1 });
        var settings = (await _admin.GetFromJsonAsync<Modules.Organization.SystemSettingsResponse>("/api/v1/settings", TestJson.Options, Ct))!;
        settings.Settings.Numbering.BauaktePattern.ShouldBe("BA-{JJ}/{NNNN}");

        var bauakte = await PostAsync<Modules.Bauakten.BauakteDetail>(_office, "/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "EFH" });
        bauakte.Number.ShouldBe($"BA-{Year % 100:00}/0001");
    }

    [Fact]
    public async Task Migration_NumbersExistingRecordsInCreationOrder_KeepsExistingNumbers()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_NumberRanges", StringComparison.Ordinal));
        await migrator.MigrateAsync(all[all.IndexOf(target) - 1], Ct);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO suppliers (id, name, is_active, version, created_at, updated_at)
            VALUES ('01900000-0000-7000-8000-000000000011', 'Zweiter', true, 1, '2024-02-01', '2024-02-01'),
                   ('01900000-0000-7000-8000-000000000012', 'Erster', true, 1, '2024-01-01', '2024-01-01');
            INSERT INTO employees (id, personnel_number, first_name, last_name, employment_type, work_days, vacation_days_per_year, vacation_carry_over,
                                   version, created_at, updated_at)
            VALUES ('01900000-0000-7000-8000-000000000021', '10001', 'Alt', 'Mit Nummer', 'FullTime', 62, 30, 0, 1, '2023-01-01', '2023-01-01'),
                   ('01900000-0000-7000-8000-000000000022', NULL, 'Neu', 'Ohne Nummer', 'FullTime', 62, 30, 0, 1, '2024-01-01', '2024-01-01');
            INSERT INTO order_number_sequences (year, last_number) VALUES (2025, 17);
            INSERT INTO bauakte_number_sequences (year, last_number) VALUES (2025, 42);
            """, Ct);

        await migrator.MigrateAsync(target, Ct);

        (await db.Database.SqlQueryRaw<string>("SELECT name || '=' || number AS \"Value\" FROM suppliers ORDER BY name").ToListAsync(Ct))
            .ShouldBe(["Erster=700001", "Zweiter=700002"]);
        (await db.Database.SqlQueryRaw<string>("SELECT first_name || '=' || personnel_number AS \"Value\" FROM employees ORDER BY first_name").ToListAsync(Ct))
            .ShouldBe(["Alt=10001", "Neu=10002"]);
        (await db.Database.SqlQueryRaw<string>("SELECT record_type || ':' || period || '=' || last_number AS \"Value\" FROM number_range_counters ORDER BY 1").ToListAsync(Ct))
            .ShouldBe(["bauakte:2025=42", "customer:0=0", "employee:0=2", "order:2025=17", "supplier:0=2"]);
        (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM number_ranges").SingleAsync(Ct)).ShouldBe(NumberRangeDefinitions.All.Count);
    }
}
