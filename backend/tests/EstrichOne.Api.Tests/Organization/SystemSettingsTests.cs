using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Organization;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Organization;

public sealed class SystemSettingsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Defaults_AreReadableForEveryone_AndOnlyAdminsSave()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var defaults = await office.GetFromJsonAsync<SystemSettingsResponse>("/api/v1/settings", TestJson.Options, Ct);
        defaults!.Settings.Session.IdleHours.ShouldBe(12);
        defaults.Settings.Map.TileUrl.ShouldStartWith("https://tile.openstreetmap.org/");

        using var denied = await office.PutAsJsonAsync("/api/v1/settings", new { settings = defaults.Settings }, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        var changed = defaults.Settings with
        {
            Session = defaults.Settings.Session with { IdleHours = 500 },
            Map = defaults.Settings.Map with { TileUrl = "http://unsicher/{z}/{x}/{y}.png" },
        };
        using var saved = await admin.PutAsJsonAsync("/api/v1/settings", new { settings = changed, version = defaults.Version }, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await saved.Content.ReadFromJsonAsync<SystemSettingsResponse>(TestJson.Options, Ct);
        body!.Settings.Session.IdleHours.ShouldBe(168);
        body.Settings.Map.TileUrl.ShouldBe("https://tile.openstreetmap.org/{z}/{x}/{y}.png");

        using var stale = await admin.PutAsJsonAsync("/api/v1/settings", new { settings = changed, version = defaults.Version }, Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }
}
