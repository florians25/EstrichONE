using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Modules.Sync;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Documentation;

public sealed class SiteRecordTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static SyncOperationResult Single(PushResponse response) => response.Results.ShouldHaveSingleItem();

    private static async Task<Guid> CreateBauakteAsync(HttpClient client)
    {
        var id = Guid.CreateVersion7();
        Single(await client.PushAsync(SyncClient.Op("bauakte.create", id, new { title = "EFH Müller" }))).Status.ShouldBe(SyncOperationStatus.Applied);
        return id;
    }

    [Fact]
    public async Task Diary_AuthorCanEdit_OthersCannot_AndOfficeSeesIt()
    {
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateBauakteAsync(site);
        var entry = Guid.CreateVersion7();

        var created = Single(await site.PushAsync(SyncClient.Op("diary.create", entry, new
        {
            bauakteId = bauakte, day = "2026-10-20", text = "Estrich im EG eingebracht, 65 mm", weather = "rain", temperatureCelsius = 9,
            workersPresent = 3,
        })));
        created.Status.ShouldBe(SyncOperationStatus.Applied);
        created.Entity.Text("weather").ShouldBe("rain");

        var foreignEdit = Single(await office.PushAsync(SyncClient.Op("diary.update", entry, new { changes = new { text = "Anders" }, @base = new { } })));
        foreignEdit.Status.ShouldBe(SyncOperationStatus.Rejected);
        foreignEdit.Code.ShouldBe("NOT_AUTHOR");

        var ownEdit = Single(await site.PushAsync(SyncClient.Op("diary.update", entry, new { changes = new { text = "Estrich im EG und OG eingebracht" }, @base = new { } })));
        ownEdit.Status.ShouldBe(SyncOperationStatus.Applied);

        var diary = await office.GetFromJsonAsync<List<DiaryEntryDto>>($"/api/v1/bauakten/{bauakte}/diary", TestJson.Options, TestContext.Current.CancellationToken);
        var item = diary.ShouldHaveSingleItem();
        item.Text.ShouldBe("Estrich im EG und OG eingebracht");
        item.Day.ShouldBe(new DateOnly(2026, 10, 20));
        item.Weather.ShouldBe(DiaryWeather.Rain);
        item.EditedAt.ShouldNotBeNull();

        var history = await office.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{bauakte}/history", TestJson.Options, TestContext.Current.CancellationToken);
        history!.Select(h => h.Text).ShouldContain("Max Huber hat das Bautagebuch für den 20.10.2026 ergänzt.");
    }

    [Fact]
    public async Task Diary_KeepsWorkTimesCrewAndPhotos_AndValidatesThem()
    {
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var bauakte = await CreateBauakteAsync(site);
        var entry = Guid.CreateVersion7();
        var photo = Guid.CreateVersion7();

        Single(await site.PushAsync(SyncClient.Op("diary.create", entry, new
        {
            bauakteId = bauakte, day = "2026-10-21", text = "Heizestrich OG", startTime = "07:00:00", endTime = "15:30:00",
            crew = "Kolonne Nord", photoFileIds = new[] { photo },
        }))).Status.ShouldBe(SyncOperationStatus.Applied);

        var invalid = Single(await site.PushAsync(SyncClient.Op("diary.update", entry, new
        {
            changes = new { startTime = "16:00:00" }, @base = new { },
        })));
        invalid.Status.ShouldBe(SyncOperationStatus.Rejected);

        Single(await site.PushAsync(SyncClient.Op("diary.update", entry, new
        {
            changes = new { crew = "Kolonne Süd", photoFileIds = Array.Empty<Guid>() }, @base = new { },
        }))).Status.ShouldBe(SyncOperationStatus.Applied);

        var item = (await site.GetFromJsonAsync<List<DiaryEntryDto>>($"/api/v1/bauakten/{bauakte}/diary", TestJson.Options,
            TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        item.StartTime.ShouldBe(new TimeOnly(7, 0));
        item.EndTime.ShouldBe(new TimeOnly(15, 30));
        item.Crew.ShouldBe("Kolonne Süd");
        item.PhotoFileIds.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("cement", false, 1.9, 2.0, true)]
    [InlineData("cement", true, 1.9, 1.8, false)]
    [InlineData("calciumSulfate", false, 0.5, 0.5, true)]
    [InlineData("calciumSulfate", true, 0.4, 0.3, false)]
    public async Task CmMeasurement_ServerDecidesReadiness(string binder, bool heated, double measured, double limit, bool ready)
    {
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var bauakte = await CreateBauakteAsync(site);

        var result = Single(await site.PushAsync(SyncClient.Op("inspection.create", Guid.CreateVersion7(), new
        {
            bauakteId = bauakte, kind = "cmMeasurement", location = "Wohnzimmer EG", binder, heated, thicknessMm = 65, measuredCmPercent = measured,
        })));

        result.Status.ShouldBe(SyncOperationStatus.Applied);
        result.Entity!["limitCmPercent"]!.GetValue<decimal>().ShouldBe((decimal)limit);
        result.Entity["isReadyForCovering"]!.GetValue<bool>().ShouldBe(ready);
        var history = await site.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{bauakte}/history", TestJson.Options, TestContext.Current.CancellationToken);
        var text = history!.Single(h => h.Action == "bauakte.inspection_added").Text;
        text.ShouldContain("CM-% bei Grenzwert");
        text.ShouldEndWith(ready ? "– belegreif." : "– noch nicht belegreif.");
    }

    [Fact]
    public async Task InitialInspection_ValidatesAndCorrectionReferencesOriginal()
    {
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var bauakte = await CreateBauakteAsync(site);

        var withoutReason = Single(await site.PushAsync(SyncClient.Op("inspection.create", Guid.CreateVersion7(), new
        {
            bauakteId = bauakte, kind = "initialInspection", notRequired = true,
        })));
        withoutReason.Status.ShouldBe(SyncOperationStatus.Rejected);
        withoutReason.Code.ShouldBe("VALIDATION_FAILED");

        var unknownCheck = Single(await site.PushAsync(SyncClient.Op("inspection.create", Guid.CreateVersion7(), new
        {
            bauakteId = bauakte, kind = "initialInspection", checks = new Dictionary<string, string> { ["kaffee"] = "ok" },
        })));
        unknownCheck.Code.ShouldBe("VALIDATION_FAILED");

        var original = Guid.CreateVersion7();
        Single(await site.PushAsync(SyncClient.Op("inspection.create", original, new
        {
            bauakteId = bauakte, kind = "initialInspection",
            checks = new Dictionary<string, string> { ["substrate"] = "ok", ["edgeInsulation"] = "notOk", ["heatingPressureTest"] = "notApplicable" },
            remarks = "Randdämmstreifen fehlen im Bad",
        }))).Status.ShouldBe(SyncOperationStatus.Applied);
        var correction = Single(await site.PushAsync(SyncClient.Op("inspection.create", Guid.CreateVersion7(), new
        {
            bauakteId = bauakte, kind = "initialInspection", correctsInspectionId = original,
            checks = new Dictionary<string, string> { ["substrate"] = "ok", ["edgeInsulation"] = "ok" },
        })));
        correction.Status.ShouldBe(SyncOperationStatus.Applied);

        var inspections = await site.GetFromJsonAsync<List<InspectionDto>>($"/api/v1/bauakten/{bauakte}/inspections", TestJson.Options, TestContext.Current.CancellationToken);
        inspections!.Count.ShouldBe(2);
        inspections.ShouldContain(i => i.CorrectsInspectionId == original && i.Checks["edgeInsulation"] == CheckResult.Ok);
        inspections.ShouldContain(i => i.Id == original && i.Checks["edgeInsulation"] == CheckResult.NotOk);
    }

    [Fact]
    public async Task ForeignBauakte_IsRejectedAndHidden()
    {
        using var owner = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var colleague = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);
        var bauakte = await CreateBauakteAsync(owner);

        var result = Single(await colleague.PushAsync(SyncClient.Op("diary.create", Guid.CreateVersion7(), new { bauakteId = bauakte, text = "x" })));
        using var list = await colleague.GetAsync(new Uri($"/api/v1/bauakten/{bauakte}/inspections", UriKind.Relative), TestContext.Current.CancellationToken);

        result.Code.ShouldBe("BAUAKTE_NOT_FOUND");
        list.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pull_DeliversDiaryAndInspections()
    {
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var bauakte = await CreateBauakteAsync(site);
        var entry = Guid.CreateVersion7();
        var inspection = Guid.CreateVersion7();
        await site.PushAsync(
            SyncClient.Op("diary.create", entry, new { bauakteId = bauakte, text = "Heizestrich, Aufheizen ab Montag" }),
            SyncClient.Op("inspection.create", inspection, new
            {
                bauakteId = bauakte, kind = "cmMeasurement", location = "Bad", binder = "cement", heated = true, measuredCmPercent = 1.7,
            }));

        var (changes, _) = await site.PullUntilAsync(null, all => all.Any(c => c.Id == entry) && all.Any(c => c.Id == inspection));

        changes.Single(c => c.Id == entry).EntityType.ShouldBe("diaryEntry");
        changes.Single(c => c.Id == inspection).Data.Text("location").ShouldBe("Bad");
    }
}
