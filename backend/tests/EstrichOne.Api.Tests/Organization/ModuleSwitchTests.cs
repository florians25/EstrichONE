using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Audit;
using EstrichOne.Api.Modules.Organization;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Organization;

/// <summary>Einstellungen › Module: Zustand lesen, schalten mit Abhängigkeiten, Sperre der Endpunkte, Protokoll.</summary>
public sealed class ModuleSwitchTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<ModulesResponse> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ModulesResponse>(TestJson.Options, Ct))!;

    [Fact]
    public async Task Modules_AreReadableForEveryone_AllOnByDefault_OnlyAdminsSwitch()
    {
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var modules = (await site.GetFromJsonAsync<ModulesResponse>("/api/v1/settings/modules", TestJson.Options, Ct))!;
        modules.Modules.Where(m => !m.Core).ShouldAllBe(m => m.Enabled);
        modules.Modules.Single(m => m.Key == AppModules.Absence).Requires.ShouldBe([AppModules.Staff]);
        modules.Modules.Single(m => m.Key == AppModules.Staff).RequiredBy.ShouldBe([AppModules.Absence]);
        modules.Modules.Single(m => m.Key == "bauakten").Core.ShouldBeTrue();

        using var denied = await site.PutAsJsonAsync("/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["calls"] = false } }, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dependencies_AreEnforced_AndExplained()
    {
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);

        using var orphan = await admin.PutAsJsonAsync("/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["staff"] = false } }, Ct);
        orphan.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await orphan.ReadProblemAsync();
        problem.Code.ShouldBe("MODULE_DEPENDENCY");
        problem.Detail.ShouldNotBeNull().ShouldContain("Urlaub und Abwesenheit");
        problem.Detail.ShouldContain("Personal");

        using var core = await admin.PutAsJsonAsync("/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["bauakten"] = false } }, Ct);
        (await core.ReadProblemAsync()).Code.ShouldBe("MODULE_CORE");
        using var unknown = await admin.PutAsJsonAsync("/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["zeitreise"] = false } }, Ct);
        (await unknown.ReadProblemAsync()).Code.ShouldBe("UNKNOWN_MODULE");

        using var both = await admin.PutAsJsonAsync("/api/v1/settings/modules",
            new { modules = new Dictionary<string, bool> { ["staff"] = false, ["absence"] = false } }, Ct);
        both.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = await ReadAsync(both);
        saved.Modules.Where(m => m.Key is AppModules.Staff or AppModules.Absence).ShouldAllBe(m => !m.Enabled);

        // Wieder einschalten: Urlaub allein geht nicht, Personal zuerst bzw. zusammen.
        using var alone = await admin.PutAsJsonAsync("/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["absence"] = true } }, Ct);
        (await alone.ReadProblemAsync()).Code.ShouldBe("MODULE_DEPENDENCY");
        using var stale = await admin.PutAsJsonAsync("/api/v1/settings/modules",
            new { modules = new Dictionary<string, bool> { ["staff"] = true }, version = saved.Version - 1 }, Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using var again = await admin.PutAsJsonAsync("/api/v1/settings/modules",
            new { modules = new Dictionary<string, bool> { ["staff"] = true, ["absence"] = true }, version = saved.Version }, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var audit = await db.Set<AuditEvent>().Where(a => a.Action == "settings.modules_changed").OrderBy(a => a.OccurredAt).ToListAsync(Ct);
        audit.Count.ShouldBe(2);
        audit[0].Details.ShouldNotBeNull().ShouldContain("absence");
        audit[1].Details.ShouldNotBeNull().ShouldContain("staff");
    }

    [Fact]
    public async Task DisabledModule_Returns409_DataStays_AndComesBack()
    {
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var callId = Guid.CreateVersion7();
        (await office.PutAsJsonAsync($"/api/v1/calls/{callId}", new { id = callId, kind = "call", callerName = "Frau Maier", subject = "Rückruf" }, Ct))
            .EnsureSuccessStatusCode();

        (await admin.PutAsJsonAsync("/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["calls"] = false, ["tasks"] = false } }, Ct))
            .EnsureSuccessStatusCode();

        using var denied = await office.GetAsync(new Uri("/api/v1/calls", UriKind.Relative), Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await denied.ReadProblemAsync();
        problem.Code.ShouldBe("MODULE_DISABLED");
        problem.Detail.ShouldNotBeNull().ShouldContain("Anrufe und Anfragen");

        // Aufgaben aus, Mängel an: Mängel gehen weiter, neue Aufgaben nicht.
        using var task = await office.PostAsJsonAsync("/api/v1/work-items", new { id = Guid.CreateVersion7(), kind = "task", title = "Aufmaß" }, Ct);
        task.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await task.ReadProblemAsync()).Code.ShouldBe("MODULE_DISABLED");
        (await office.PostAsJsonAsync("/api/v1/work-items", new { id = Guid.CreateVersion7(), kind = "defect", title = "Riss" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        (await admin.PutAsJsonAsync("/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["calls"] = true, ["tasks"] = true } }, Ct))
            .EnsureSuccessStatusCode();
        using var back = await office.GetAsync(new Uri($"/api/v1/calls/{callId}", UriKind.Relative), Ct);
        back.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EveryModuleEndpoint_WhileModuleIsOff_Returns409()
    {
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        var off = AppModules.Switchable.ToDictionary(m => m.Key, _ => false);
        (await admin.PutAsJsonAsync("/api/v1/settings/modules", new { modules = off }, Ct)).EnsureSuccessStatusCode();

        // Alle Rechte außer „Einstellungen verwalten“ (Administratoren dürfen vorhandene Daten weiter lesen).
        var roleId = Guid.CreateVersion7();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            db.Add(Role.Create(roleId, "Alles außer Einstellungen", null, false,
                EstrichOne.Api.Modules.Access.Exports.Permissions.All.Select(p => p.Code)
                    .Where(c => c != EstrichOne.Api.Modules.Access.Exports.Permissions.SettingsManage)));
            await db.SaveChangesAsync(Ct);
        }

        using var user = await _factory.CreateClientAsAsync("alles", roleId);
        var gated = _factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ModuleRequirement>() is not null)
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => (m, e)))
            .ToList();
        gated.Count.ShouldBeGreaterThan(80);
        var problems = new List<string>();
        foreach (var (method, endpoint) in gated)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), Path(endpoint));
            if (method is not ("GET" or "DELETE"))
            {
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            }

            using var response = await user.SendAsync(request, Ct);
            var settingsOnly = endpoint.RoutePattern.RawText == "/api/v1/archive/dossiers/{bauakteId:guid}" && method == "POST"; // braucht settings.manage
            if (response.StatusCode != (settingsOnly ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict))
            {
                problems.Add($"{method} {endpoint.RoutePattern.RawText}: {(int)response.StatusCode}");
            }
        }

        problems.ShouldBeEmpty();
        (await admin.GetAsync(new Uri("/api/v1/materials", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"/api/v1/suppliers/{Guid.CreateVersion7()}", new { name = "Maier" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SavingAllSettings_NormalizesDependencies_AndRecordsTheChange()
    {
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        var settings = (await admin.GetFromJsonAsync<SystemSettingsResponse>("/api/v1/settings", TestJson.Options, Ct))!;
        var changed = settings.Settings with { Modules = settings.Settings.Modules with { Materials = false, Planning = false } };
        using var saved = await admin.PutAsJsonAsync("/api/v1/settings", new { settings = changed, version = settings.Version }, Ct);
        var body = (await saved.Content.ReadFromJsonAsync<SystemSettingsResponse>(TestJson.Options, Ct))!;
        body.Settings.Modules.Orders.ShouldBeFalse();
        body.Settings.Modules.Machines.ShouldBeFalse();
        body.Settings.Modules.Staff.ShouldBeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var entry = await db.Set<AuditEvent>().SingleAsync(a => a.Action == "settings.modules_changed", Ct);
        entry.Details.ShouldNotBeNull().ShouldContain("orders");
    }

    [Fact]
    public void Notifications_OfDisabledModules_AreDropped()
    {
        var modules = new ModuleSettings { Planning = false, Tasks = false };
        modules.AllowsNotification("planning.released").ShouldBeFalse();
        modules.AllowsNotification("work.assigned").ShouldBeTrue(); // Mängel sind noch an
        (modules with { Defects = false }).AllowsNotification("work.assigned").ShouldBeFalse();
        modules.AllowsNotification("backup.failed").ShouldBeTrue();
        modules.AllowsNotification(null).ShouldBeTrue();
        new ModuleSettings { Staff = false }.Normalize().Absence.ShouldBeFalse();
    }

    private static string Path(Microsoft.AspNetCore.Routing.RouteEndpoint endpoint) =>
        "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal => literal.Content,
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternSeparatorPart separator => separator.Content,
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart parameter when parameter.ParameterPolicies.Any(p => p.Content == "guid") =>
                Guid.CreateVersion7().ToString(),
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart parameter when parameter.Name is "day" => "2026-11-02",
            Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart => "x",
            _ => string.Empty,
        }))));
}
