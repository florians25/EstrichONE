using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Organization;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Bauakten;

/// <summary>
/// „Hinweise und Zugang“: Das frühere Feld „Zugang / Besonderheiten“ (project.siteAccess) ist in die Hinweise (siteNotes)
/// aufgegangen. Ältere Apps dürfen es weiter schreiben; der Server hängt den Text an und liefert siteAccess immer leer.
/// </summary>
public sealed class SiteNotesAndAccessTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<BauakteDetail> CreateAsync(HttpClient client, string title, object? project = null)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title, project }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
    }

    private static Task<BauakteDetail> GetAsync(HttpClient client, Guid id) =>
        client.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{id}", TestJson.Options, Ct)!;

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, BauakteDetail b, string? siteNotes, object? project)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/bauakten/{b.Id}")
        {
            Content = JsonContent.Create(new { title = b.Title, status = b.Status.ToString(), siteNotes, project }),
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{b.Version}\""));
        return await client.SendAsync(request, Ct);
    }

    [Theory]
    [InlineData(null, "Pumpe vorne", "Pumpe vorne")]
    [InlineData("Hund im Hof", "Pumpe vorne", "Hund im Hof\n\nPumpe vorne")]
    [InlineData("Hund im Hof\n\nPumpe vorne", "  Pumpe vorne ", "Hund im Hof\n\nPumpe vorne")]
    [InlineData("Hund im Hof", "  ", "Hund im Hof")]
    [InlineData(" ", null, null)]
    public void MergeSiteAccess_AppendsWithBlankLine_WithoutDuplicates(string? notes, string? access, string? expected) =>
        Bauakte.MergeSiteAccess(notes, access).ShouldBe(expected);

    [Fact]
    public void MergeSiteAccess_KeepsEverything_WhenTheBlankLineNoLongerFits()
    {
        var notes = new string('a', 2000);
        var access = new string('b', 999);
        Bauakte.MergeSiteAccess(notes, access).ShouldBe(notes + "\n" + access);
        Bauakte.MergeSiteAccess(notes, access + "b").ShouldBe(notes + access + "b");
    }

    [Fact]
    public async Task OlderClient_WritingSiteAccess_LandsInSiteNotes_AndSiteAccessStaysEmpty()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var created = await CreateAsync(office, "EFH Kraus", new { floors = "EG", siteAccess = "Pumpe am Straßenrand" });
        created.SiteNotes.ShouldBe("Pumpe am Straßenrand");
        created.Project!.SiteAccess.ShouldBeNull();
        created.Project.Floors.ShouldBe("EG");

        using var response = await PutAsync(office, created, "Hund im Hof", new { floors = "EG, OG", siteAccess = "Zufahrt nur bis 7,5 t" });
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
        saved.SiteNotes.ShouldBe("Hund im Hof\n\nZufahrt nur bis 7,5 t");
        saved.Project!.SiteAccess.ShouldBeNull();
        saved.Project.Floors.ShouldBe("EG, OG");

        // Dasselbe noch einmal (Gerät mit altem Stand): nichts wird doppelt angehängt.
        using var again = await PutAsync(office, saved, saved.SiteNotes, new { floors = "EG, OG", siteAccess = "Zufahrt nur bis 7,5 t" });
        again.EnsureSuccessStatusCode();
        var read = await GetAsync(office, created.Id);
        read.SiteNotes.ShouldBe("Hund im Hof\n\nZufahrt nur bis 7,5 t");
        read.Project!.SiteAccess.ShouldBeNull();
    }

    [Fact]
    public async Task SiteNotes_AcceptUpTo3000Characters_LongerTextsAreRejected()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateAsync(office, "Halle");

        using var ok = await PutAsync(office, bauakte, new string('x', 3000), null);
        ok.EnsureSuccessStatusCode();
        var saved = (await ok.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
        saved.SiteNotes!.Length.ShouldBe(3000);

        using var tooLong = await PutAsync(office, saved, new string('x', 3001), null);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Ältere App hängt „Zugang / Besonderheiten“ an volle Hinweise an: abgelehnt, nichts wird abgeschnitten.
        using var overflow = await PutAsync(office, saved, saved.SiteNotes, new { siteAccess = "Pumpe vorne" });
        overflow.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await GetAsync(office, bauakte.Id)).SiteNotes!.Length.ShouldBe(3000);
    }

    [Fact]
    public async Task Sync_OlderApp_SiteAccessIsAppendedToTheNotesOfTheSameOperation()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var id = Guid.CreateVersion7();
        // Anlage: neue Apps senden siteNotes, ältere project.siteAccess; beides zusammen wird nicht doppelt gespeichert.
        var created = (await worker.PushAsync(SyncClient.Op("bauakte.create", id,
            new { title = "Anbau", siteNotes = "Schlüssel beim Nachbarn", project = new { siteAccess = "Schlüssel beim Nachbarn" } }))).Results.Single();
        created.Status.ShouldBe(SyncOperationStatus.Applied);
        created.Entity!["siteNotes"]!.GetValue<string>().ShouldBe("Schlüssel beim Nachbarn");
        created.Entity["project"]!["siteAccess"].ShouldBeNull();

        // „project“ steht vor „siteNotes“: Der Zugang darf trotzdem nicht von den neuen Hinweisen überschrieben werden.
        var update = (await worker.PushAsync(SyncClient.Op("bauakte.update", id, new
        {
            changes = new { project = new { floors = "EG", siteAccess = "Pumpe hinten" }, siteNotes = "Hund im Hof" },
            @base = new { project = created.Entity["project"], siteNotes = "Schlüssel beim Nachbarn" },
        }))).Results.Single();

        update.Status.ShouldBe(SyncOperationStatus.Applied);
        update.Entity!["siteNotes"]!.GetValue<string>().ShouldBe("Hund im Hof\n\nPumpe hinten");
        update.Entity["project"]!["siteAccess"].ShouldBeNull();
        update.Entity["project"]!["floors"]!.GetValue<string>().ShouldBe("EG");

        // Gerät mit dem Stand von vor der Umstellung ändert nur die Hinweise: Konflikt, der Server behält den vollen Text.
        var stale = (await worker.PushAsync(SyncClient.Op("bauakte.update", id, new
        {
            changes = new { siteNotes = "Nur noch das" }, @base = new { siteNotes = "Hund im Hof" },
        }))).Results.Single();
        stale.Status.ShouldBe(SyncOperationStatus.Conflict);
        stale.Entity!["siteNotes"]!.GetValue<string>().ShouldBe("Hund im Hof\n\nPumpe hinten");
    }

    [Fact]
    public async Task FormSchema_HasNoSiteAccessField_AndIgnoresItOnSave()
    {
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        var schema = (await admin.GetFromJsonAsync<FormSchemaResponse>("/api/v1/forms/bauakte", TestJson.Options, Ct))!;
        schema.Groups.SelectMany(g => g.Fields).ShouldNotContain(f => f.Key == "siteAccess");
        schema.Groups.Single(g => g.Key == "baustelle").Fields.Select(f => f.Key).ShouldBe(["waterOnSite", "powerOnSite"]);

        var groups = schema.Groups.Select(g => g.Key != "baustelle" ? g
            : g with { Fields = [new FormSchemaField("siteAccess", "Zugang / Besonderheiten", "textarea", "project.siteAccess", Builtin: true), .. g.Fields] }).ToList();
        using var saved = await admin.PutAsJsonAsync("/api/v1/forms/bauakte", new { groups }, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await saved.Content.ReadFromJsonAsync<FormSchemaResponse>(TestJson.Options, Ct))!.Groups.SelectMany(g => g.Fields)
            .ShouldNotContain(f => f.Key == "siteAccess");
    }

    [Fact]
    public async Task Migration_MovesSiteAccessIntoSiteNotes_RemovesTheSchemaField_AndIsRepeatable()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var both = await CreateAsync(office, "Beides gefüllt");
        var onlyAccess = await CreateAsync(office, "Nur Zugang");
        var contained = await CreateAsync(office, "Schon enthalten");
        var onlyNotes = await CreateAsync(office, "Nur Hinweise");
        var longTexts = await CreateAsync(office, "Volle Länge");
        var notes2000 = new string('n', 2000);
        var access1000 = new string('z', 1000);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_SiteNotesAndAccess", StringComparison.Ordinal));

        // Stand vor der Umstellung herstellen: Spalte wieder 2000 Zeichen, siteAccess in project, Feld im Formular.
        await migrator.MigrateAsync(all[all.IndexOf(target) - 1], Ct);
        await db.Database.ExecuteSqlAsync($$"""
            UPDATE bauakten SET site_notes = 'Hund im Hof', project = '{"floors":"EG","siteAccess":" Pumpe vorne "}'::jsonb WHERE id = {{both.Id}};
            UPDATE bauakten SET site_notes = NULL, project = '{"siteAccess":"Zufahrt über den Hof"}'::jsonb WHERE id = {{onlyAccess.Id}};
            UPDATE bauakten SET site_notes = 'Achtung: Zufahrt über den Hof, Hund', project = '{"siteAccess":"Zufahrt über den Hof"}'::jsonb WHERE id = {{contained.Id}};
            UPDATE bauakten SET site_notes = 'Schlüssel im Büro', project = '{"floors":"OG"}'::jsonb WHERE id = {{onlyNotes.Id}};
            UPDATE bauakten SET site_notes = {{notes2000}}, project = jsonb_build_object('siteAccess', {{access1000}}::text) WHERE id = {{longTexts.Id}};
            UPDATE form_schemas SET definition = '[{"key":"bauvorhaben","label":"Bauvorhaben","fields":[{"key":"floors","label":"Geschosse","type":"text","path":"project.floors","builtin":true}]},{"key":"baustelle","label":"Baustelle","fields":[{"key":"siteAccess","label":"Zugang / Besonderheiten","type":"textarea","path":"project.siteAccess","builtin":true},{"key":"waterOnSite","label":"Wasser vor Ort","type":"boolean","path":"project.waterOnSite","builtin":true}]}]'::jsonb
                WHERE key = 'bauakte';
            """, Ct);
        var versionsBefore = await VersionsAsync(db);
        // Der ältere Stand kennt die Spalten späterer Migrationen nicht; für das Lesen des Änderungsfeeds kurz ergänzen.
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE bauakten ADD COLUMN review_requested_at timestamptz, ADD COLUMN review_reason varchar(300), ADD COLUMN review_stamp varchar(1000)", Ct);
        var cursor = (await office.PullUntilAsync(null, changes => changes.Any(c => c.Id == longTexts.Id))).Cursor;
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE bauakten DROP COLUMN review_requested_at, DROP COLUMN review_reason, DROP COLUMN review_stamp", Ct);

        await migrator.MigrateAsync(cancellationToken: Ct);
        await AssertMigratedAsync();
        var versionsAfter = await VersionsAsync(db);
        versionsAfter[both.Id].ShouldBe(versionsBefore[both.Id] + 1);
        versionsAfter[onlyNotes.Id].ShouldBe(versionsBefore[onlyNotes.Id]);

        // Apps holen den neuen Stand über den Änderungsfeed ab.
        var pulled = (await office.PullUntilAsync(cursor, changes => changes.Any(c => c.Id == both.Id) && changes.Any(c => c.EntityType == "formSchema"))).Changes;
        pulled.ShouldContain(c => c.EntityType == "bauakte" && c.Id == both.Id);
        pulled.ShouldContain(c => c.EntityType == "formSchema");
        pulled.ShouldNotContain(c => c.Id == onlyNotes.Id);

        // Wiederholbar: Ein zweiter Lauf ändert nichts mehr.
        (await db.Database.ExecuteSqlRawAsync(SiteNotesAccessMerge.Bauakten, Ct)).ShouldBe(0);
        (await db.Database.ExecuteSqlRawAsync(SiteNotesAccessMerge.FormSchemas, Ct)).ShouldBe(0);
        await AssertMigratedAsync();
        (await VersionsAsync(db)).ShouldBe(versionsAfter);

        async Task AssertMigratedAsync()
        {
            var merged = await GetAsync(office, both.Id);
            merged.SiteNotes.ShouldBe("Hund im Hof\n\nPumpe vorne");
            merged.Project!.SiteAccess.ShouldBeNull();
            merged.Project.Floors.ShouldBe("EG");
            (await GetAsync(office, onlyAccess.Id)).SiteNotes.ShouldBe("Zufahrt über den Hof");
            (await GetAsync(office, contained.Id)).SiteNotes.ShouldBe("Achtung: Zufahrt über den Hof, Hund");
            var untouched = await GetAsync(office, onlyNotes.Id);
            untouched.SiteNotes.ShouldBe("Schlüssel im Büro");
            untouched.Project!.Floors.ShouldBe("OG");
            (await GetAsync(office, longTexts.Id)).SiteNotes.ShouldBe(notes2000 + access1000);
            (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM bauakten WHERE jsonb_exists(project, 'siteAccess')").SingleAsync(Ct)).ShouldBe(0);

            var schema = (await office.GetFromJsonAsync<FormSchemaResponse>("/api/v1/forms/bauakte", TestJson.Options, Ct))!;
            schema.Groups.Select(g => g.Key).ShouldBe(["bauvorhaben", "baustelle"]);
            schema.Groups[0].Fields.Select(f => f.Key).ShouldBe(["floors"]);
            schema.Groups[1].Fields.Select(f => f.Key).ShouldBe(["waterOnSite"]);
        }
    }

    // Ohne die Entität: Vor der Migration fehlen Spalten späterer Migrationen (z. B. review_requested_at).
    private static async Task<Dictionary<Guid, long>> VersionsAsync(EstrichOneDbContext db) =>
        await db.Database.SqlQueryRaw<VersionRow>("SELECT id AS \"Id\", version AS \"Version\" FROM bauakten").ToDictionaryAsync(b => b.Id, b => b.Version, Ct);

    private sealed class VersionRow
    {
        public Guid Id { get; set; }
        public long Version { get; set; }
    }
}
