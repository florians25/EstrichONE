using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Sync;

/// <summary>Status ändern und stornieren aus der App (bauakte.setStatus), auch offline erfasst.</summary>
public sealed class StatusSyncTests(PostgresFixture postgres) : IAsyncLifetime
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

    private async Task<Guid> BauakteAsync()
    {
        var id = Guid.CreateVersion7();
        (await _site.PushAsync(SyncClient.Op("bauakte.create", id, new { title = "Status" }))).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        return id;
    }

    private static async Task<PushResponseView> SetAsync(HttpClient client, Guid id, object payload) =>
        new((await client.PushAsync(SyncClient.Op("bauakte.setStatus", id, payload))).Results.Single());

    private sealed record PushResponseView(SyncOperationResult Result);

    [Fact]
    public async Task StatusChange_AndCancellationWithReason_AreApplied()
    {
        var id = await BauakteAsync();

        var started = (await SetAsync(_site, id, new { status = "inProgress", baseStatus = "planned" })).Result;
        started.Status.ShouldBe(SyncOperationStatus.Applied);
        started.Entity.Text("status").ShouldBe("inProgress");

        var withoutReason = (await SetAsync(_site, id, new { status = "cancelled", baseStatus = "inProgress" })).Result;
        withoutReason.Status.ShouldBe(SyncOperationStatus.Rejected);
        withoutReason.Code.ShouldBe("VALIDATION_FAILED");

        var cancelled = (await SetAsync(_site, id, new { status = "cancelled", baseStatus = "inProgress", reason = "  Kunde hat abgesagt " })).Result;
        cancelled.Status.ShouldBe(SyncOperationStatus.Applied);
        cancelled.Entity.Text("status").ShouldBe("cancelled");
        cancelled.Entity.Text("cancellationReason").ShouldBe("Kunde hat abgesagt");
    }

    [Fact]
    public async Task CancellationWins_AndOutdatedStatusChangesAreRejected()
    {
        var id = await BauakteAsync();
        (await SetAsync(_office, id, new { status = "drying", baseStatus = "planned" })).Result.Status.ShouldBe(SyncOperationStatus.Applied);

        // Gerät war noch auf „Geplant“: abgelehnt mit Erklärung statt Überschreiben.
        var outdated = (await SetAsync(_site, id, new { status = "inProgress", baseStatus = "planned" })).Result;
        outdated.Status.ShouldBe(SyncOperationStatus.Rejected);
        outdated.Code.ShouldBe("STATUS_CHANGED");
        outdated.Message!.ShouldContain("Belegreif");

        (await SetAsync(_office, id, new { status = "cancelled", baseStatus = "drying", reason = "Auftrag storniert" })).Result.Status
            .ShouldBe(SyncOperationStatus.Applied);
        var afterCancel = (await SetAsync(_site, id, new { status = "completed", baseStatus = "drying" })).Result;
        afterCancel.Status.ShouldBe(SyncOperationStatus.Rejected);
        afterCancel.Code.ShouldBe("ALREADY_CANCELLED");

        // Wer die Stornierung kennt, darf bewusst reaktivieren.
        (await SetAsync(_office, id, new { status = "planned", baseStatus = "cancelled" })).Result.Status.ShouldBe(SyncOperationStatus.Applied);
    }

    [Fact]
    public async Task WithoutEditPermission_IsDenied_AndUnknownStatusIsInvalid()
    {
        var id = await BauakteAsync();
        using var it = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);

        (await SetAsync(it, id, new { status = "completed" })).Result.Code.ShouldBe("PERMISSION_DENIED");
        (await SetAsync(_site, id, new { status = "explodiert" })).Result.Code.ShouldBe("VALIDATION_FAILED");
    }
}
