using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Sync;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EstrichOne.Api.Tests.Sync;

public sealed class SyncProtocolTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _site = null!;
    private HttpClient _office = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        _office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
    }

    public async ValueTask DisposeAsync()
    {
        _site.Dispose();
        _office.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task OfflineCreatedBauakte_WithNoteTaskAndPhoto_ArrivesInOnePushAndInThePull()
    {
        var bauakte = Guid.CreateVersion7();
        var note = Guid.CreateVersion7();
        var task = Guid.CreateVersion7();
        var file = Guid.CreateVersion7();

        var result = await _site.PushAsync(
            SyncClient.Op("bauakte.create", bauakte, new { title = "EFH Müller, Neubau", city = "Landshut" }),
            SyncClient.Op("note.create", note, new { bauakteId = bauakte, text = "Estrich am Freitag" }),
            SyncClient.Op("workitem.create", task, new { bauakteId = bauakte, kind = "defect", title = "Riss im Flur" }),
            SyncClient.Op("file.announce", file, new { bauakteId = bauakte, kind = "photo", originalName = "IMG_1.jpg",
                contentType = "image/jpeg", sizeBytes = 1234, sha256 = new string('a', 64) }));

        result.Results.Select(r => r.Status).ShouldAllBe(status => status == SyncOperationStatus.Applied);
        result.Results[0].Entity.Text("number").ShouldNotBeNullOrEmpty();

        Guid[] ours = [bauakte, note, task, file];
        var (changes, cursor) = await _site.PullUntilAsync(null, all => ours.All(id => all.Any(c => c.Id == id)));
        changes.Select(c => c.EntityType).ShouldBeSubsetOf(["bauakte", "note", "workItem", "file", "screedType", "contact"]);
        changes.Single(c => c.Id == bauakte).Data.Text("title").ShouldBe("EFH Müller, Neubau");
        changes.Single(c => c.Id == note).Data.Text("text").ShouldBe("Estrich am Freitag");

        var again = await _site.PullAsync(cursor);
        again.Changes.ShouldNotContain(c => ours.Contains(c.Id), "Nach dem Cursor wird nichts erneut ausgeliefert.");
    }

    [Fact]
    public async Task RepeatedPush_IsIdempotent()
    {
        var bauakte = Guid.CreateVersion7();
        var op = SyncClient.Op("bauakte.create", bauakte, new { title = "Halle" }, opId: Guid.CreateVersion7());

        var first = await _site.PushAsync(op);
        var second = await _site.PushAsync(op);

        first.Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        second.Results.Single().Status.ShouldBe(SyncOperationStatus.Duplicate);
        second.Results.Single().Entity.Text("number").ShouldBe(first.Results.Single().Entity.Text("number"));
        await using var scope = _factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM bauakten").SingleAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task ChangesToDifferentFields_AreBothApplied()
    {
        var bauakte = await CreateAsync(_site, "Reihenhaus");

        var office = await _office.PushAsync(SyncClient.Op("bauakte.update", bauakte, new
        {
            changes = new { title = "Reihenhaus Mitte" },
            @base = new { title = "Reihenhaus" },
        }));
        var site = await _site.PushAsync(SyncClient.Op("bauakte.update", bauakte, new
        {
            changes = new { street = "Musterstr. 12" },
            @base = new { street = (string?)null },
        }));

        office.Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        site.Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        site.Results.Single().Entity.Text("title").ShouldBe("Reihenhaus Mitte");
        site.Results.Single().Entity.Text("street").ShouldBe("Musterstr. 12");
    }

    [Fact]
    public async Task SameFieldChangedDifferently_IsAConflictAndServerKeepsItsValue()
    {
        var bauakte = await CreateAsync(_site, "Schule");
        await _office.PushAsync(SyncClient.Op("bauakte.update", bauakte, new { changes = new { city = "Landshut" }, @base = new { city = (string?)null } }));

        var result = (await _site.PushAsync(SyncClient.Op("bauakte.update", bauakte, new
        {
            changes = new { city = "Ergolding", description = "Zufahrt über Hof" },
            @base = new { city = (string?)null, description = (string?)null },
        }))).Results.Single();

        result.Status.ShouldBe(SyncOperationStatus.Conflict);
        var conflict = result.Conflicts!.ShouldHaveSingleItem();
        conflict.Field.ShouldBe("city");
        conflict.ServerValue!.GetValue<string>().ShouldBe("Landshut");
        result.Entity.Text("city").ShouldBe("Landshut");
        result.Entity.Text("description").ShouldBe("Zufahrt über Hof", "Nicht betroffene Felder werden trotzdem übernommen.");
    }

    [Fact]
    public async Task NoteForInvisibleBauakte_IsRejectedAndRememberedForRetries()
    {
        var foreign = await CreateAsync(_office, "Bürobaustelle");
        var op = SyncClient.Op("note.create", Guid.CreateVersion7(), new { bauakteId = foreign, text = "Hallo" }, opId: Guid.CreateVersion7());

        var first = (await _site.PushAsync(op)).Results.Single();
        var retry = (await _site.PushAsync(op)).Results.Single();

        first.Status.ShouldBe(SyncOperationStatus.Rejected);
        first.Code.ShouldBe("NOT_FOUND");
        retry.Status.ShouldBe(SyncOperationStatus.Duplicate);
        retry.Code.ShouldBe("NOT_FOUND");
    }

    [Fact]
    public async Task UnknownOperationsAndInvalidPayloads_AreRejectedIndividually()
    {
        var results = (await _site.PushAsync(
            SyncClient.Op("bauakte.explode", Guid.CreateVersion7(), new { }),
            SyncClient.Op("bauakte.create", Guid.CreateVersion7(), new { title = "  " }),
            SyncClient.Op("bauakte.create", Guid.CreateVersion7(), new { title = "Gültig" }))).Results;

        results[0].Code.ShouldBe("UNKNOWN_OPERATION");
        results[1].Code.ShouldBe("VALIDATION_FAILED");
        results[2].Status.ShouldBe(SyncOperationStatus.Applied);
    }

    [Fact]
    public async Task OpIdOfAnotherUser_IsNotReplayed()
    {
        var opId = Guid.CreateVersion7();
        await _site.PushAsync(SyncClient.Op("bauakte.create", Guid.CreateVersion7(), new { title = "A" }, opId));

        var result = (await _office.PushAsync(SyncClient.Op("bauakte.create", Guid.CreateVersion7(), new { title = "B" }, opId))).Results.Single();

        result.Code.ShouldBe("OP_ID_CONFLICT");
    }

    [Fact]
    public async Task Pull_OnlyDeliversVisibleBauakten_AndReportsScope()
    {
        var mine = await CreateAsync(_site, "Meine");
        var foreign = await CreateAsync(_office, "Fremde");

        var (changes, _) = await _site.PullUntilAsync(null, all => all.Any(c => c.Id == mine));
        var scope = await _site.PullAsync(includeScope: true);

        changes.ShouldNotContain(c => c.Id == foreign);
        scope.VisibleBauakteIds.ShouldNotBeNull();
        scope.VisibleBauakteIds.ShouldContain(mine);
        scope.VisibleBauakteIds.ShouldNotContain(foreign);
    }

    [Fact]
    public async Task DeletedBauakte_ArrivesAsTombstone()
    {
        var bauakte = await CreateAsync(_office, "Wird gelöscht");
        var (_, cursor) = await _office.PullUntilAsync(null, all => all.Any(c => c.Id == bauakte));

        (await _office.PushAsync(SyncClient.Op("bauakte.delete", bauakte, new { }))).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);

        var (changes, _) = await _office.PullUntilAsync(cursor, all => all.Any(c => c.Id == bauakte));
        var tombstone = changes.Single(c => c.Id == bauakte);
        tombstone.Deleted.ShouldBeTrue();
        tombstone.Data.ShouldBeNull();
    }

    [Fact]
    public async Task Pull_NeverSkipsAChangeCommittedOutOfOrder()
    {
        var first = await CreateAsync(_office, "Erste");
        var second = await CreateAsync(_office, "Zweite");
        var (_, cursor) = await _office.PullUntilAsync(null, all => all.Any(c => c.Id == second));
        var connectionString = _factory.Services.GetRequiredService<EstrichOneDbContext>().Database.GetConnectionString()!;

        // A beginnt zuerst (kleinere Transaktions-ID), schreibt aber erst nach B fest.
        await using var a = new NpgsqlConnection(connectionString);
        await a.OpenAsync(TestContext.Current.CancellationToken);
        await using var transactionA = await a.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var updateA = new NpgsqlCommand($"UPDATE bauakten SET description = 'A' WHERE id = '{first}'", a, transactionA))
        {
            await updateA.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using (var b = new NpgsqlConnection(connectionString))
        {
            await b.OpenAsync(TestContext.Current.CancellationToken);
            await using var updateB = new NpgsqlCommand($"UPDATE bauakten SET description = 'B' WHERE id = '{second}'", b);
            await updateB.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var whileAOpen = await _office.PullAsync(cursor);
        whileAOpen.Changes.ShouldBeEmpty("B darf nicht vor dem noch offenen, früher begonnenen A ausgeliefert werden.");
        whileAOpen.Cursor.ShouldBe(cursor);

        await transactionA.CommitAsync(TestContext.Current.CancellationToken);
        var (changes, _) = await _office.PullUntilAsync(cursor, all => all.Count(c => c.Id == first || c.Id == second) == 2);
        changes.Single(c => c.Id == first).Data.Text("description").ShouldBe("A");
        changes.Single(c => c.Id == second).Data.Text("description").ShouldBe("B");
    }

    [Fact]
    public async Task Pull_AfterRestore_RequiresFullResync()
    {
        await CreateAsync(_office, "Vor der Wiederherstellung");
        var (_, cursor) = await _office.PullUntilAsync(null, all => all.Count > 0);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Database
                .ExecuteSqlRawAsync("UPDATE system_state SET instance_epoch = gen_random_uuid()", TestContext.Current.CancellationToken);
        }

        using var response = await _office.GetAsync(new Uri("/api/v1/sync/pull?cursor=" + Uri.EscapeDataString(cursor), UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Code.ShouldBe("RESYNC_REQUIRED");
    }

    [Fact]
    public async Task OperationsWithoutPermission_AreRejected()
    {
        using var noRole = await _factory.CreateClientAsAsync("ohne.rolle");

        var result = (await noRole.PushAsync(SyncClient.Op("workitem.create", Guid.CreateVersion7(), new { kind = "task", title = "x" }))).Results.Single();

        result.Code.ShouldBe("PERMISSION_DENIED");
    }

    [Fact]
    public async Task EveryEntitySource_HasAChangeTrigger()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var triggers = await db.Database.SqlQuery<string>($"SELECT tgname::text AS \"Value\" FROM pg_trigger WHERE tgname LIKE '%_change_log'")
            .ToListAsync(TestContext.Current.CancellationToken);
        var sources = scope.ServiceProvider.GetServices<ISyncEntitySource>().Select(s => s.EntityType).ToList();

        SyncModule.TrackedTables.Select(t => t.EntityType).ShouldBe(sources, ignoreOrder: true);
        triggers.ShouldBe(SyncModule.TrackedTables.Select(t => t.Table + "_change_log"), ignoreOrder: true);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string title)
    {
        var id = Guid.CreateVersion7();
        (await client.PushAsync(SyncClient.Op("bauakte.create", id, new { title }))).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        return id;
    }
}
