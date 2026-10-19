using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Tests.Bauakten;

public sealed class BauakteModelTests(PostgresFixture postgres) : IAsyncLifetime
{
    private DatabaseScope _database = null!;

    public async ValueTask InitializeAsync() => _database = await DatabaseScope.CreateAsync(postgres);

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public void Create_WithOnlyATitle_IsValidAndMakesCreatorResponsibleMember()
    {
        var creator = Guid.CreateVersion7();

        var bauakte = Bauakte.Create(Guid.CreateVersion7(), "  EFH Müller, Neubau  ", creator);

        bauakte.Title.ShouldBe("EFH Müller, Neubau");
        bauakte.Number.ShouldBeNull();
        bauakte.CustomerId.ShouldBeNull();
        bauakte.ResponsibleUserId.ShouldBe(creator);
        bauakte.Members.ShouldHaveSingleItem().Role.ShouldBe(BauakteMemberRole.Responsible);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutTitle_IsRejected(string title) =>
        Should.Throw<ArgumentException>(() => Bauakte.Create(Guid.CreateVersion7(), title, Guid.CreateVersion7()));

    [Fact]
    public void AssignNumber_Twice_IsRejected()
    {
        var bauakte = Bauakte.Create(Guid.CreateVersion7(), "Halle", Guid.CreateVersion7());
        bauakte.AssignNumber("2026-001");

        Should.Throw<InvalidOperationException>(() => bauakte.AssignNumber("2026-002"));
    }

    [Fact]
    public async Task Save_SetsVersionAndTimestamps_AndIncrementsVersionOnChange()
    {
        var user = await _database.CreateUserAsync();
        var bauakte = Bauakte.Create(Guid.CreateVersion7(), "MFH Landshut", user.Id);
        await _database.WithDbAsync(async db =>
        {
            db.Add(bauakte);
            await db.SaveChangesAsync();
        });

        var created = await _database.WithDbAsync(db => db.Set<Bauakte>().SingleAsync(b => b.Id == bauakte.Id));
        created.Version.ShouldBe(1);
        created.CreatedAt.ShouldBe(created.UpdatedAt);

        await _database.WithDbAsync(async db =>
        {
            var loaded = await db.Set<Bauakte>().SingleAsync(b => b.Id == bauakte.Id);
            loaded.ChangeStatus(BauakteStatus.InProgress);
            await db.SaveChangesAsync();
        });

        var updated = await _database.WithDbAsync(db => db.Set<Bauakte>().SingleAsync(b => b.Id == bauakte.Id));
        updated.Version.ShouldBe(2);
        updated.Status.ShouldBe(BauakteStatus.InProgress);
        updated.CreatedAt.ShouldBe(created.CreatedAt);
    }

    [Fact]
    public async Task Save_WithStaleVersion_IsRejectedAsConcurrencyConflict()
    {
        var user = await _database.CreateUserAsync();
        var id = Guid.CreateVersion7();
        await _database.WithDbAsync(async db =>
        {
            db.Add(Bauakte.Create(id, "Reihenhaus", user.Id));
            await db.SaveChangesAsync();
        });

        await _database.WithDbAsync(async office =>
        {
            var officeCopy = await office.Set<Bauakte>().SingleAsync(b => b.Id == id);

            await _database.WithDbAsync(async site =>
            {
                var siteCopy = await site.Set<Bauakte>().SingleAsync(b => b.Id == id);
                siteCopy.Describe("Geändert auf der Baustelle");
                await site.SaveChangesAsync();
            });

            officeCopy.Describe("Geändert im Büro");
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => office.SaveChangesAsync());
        });
    }

    [Fact]
    public async Task Delete_KeepsTombstone_HiddenFromNormalQueries()
    {
        var user = await _database.CreateUserAsync();
        var id = Guid.CreateVersion7();
        await _database.WithDbAsync(async db =>
        {
            var bauakte = Bauakte.Create(id, "Tiefgarage", user.Id);
            db.Add(bauakte);
            await db.SaveChangesAsync();
            bauakte.Delete(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        });

        var visible = await _database.WithDbAsync(db => db.Set<Bauakte>().AnyAsync(b => b.Id == id));
        var tombstone = await _database.WithDbAsync(db =>
            db.Set<Bauakte>().IgnoreQueryFilters([QueryFilters.NotDeleted]).SingleAsync(b => b.Id == id));

        visible.ShouldBeFalse();
        tombstone.DeletedAt.ShouldNotBeNull();
        tombstone.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Save_WithUnknownCustomer_IsRejectedByForeignKey()
    {
        var user = await _database.CreateUserAsync();
        var bauakte = Bauakte.Create(Guid.CreateVersion7(), "Schule", user.Id);
        bauakte.AssignCustomer(Guid.CreateVersion7());

        await _database.WithDbAsync(async db =>
        {
            db.Add(bauakte);
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }

    [Fact]
    public async Task Rooms_StoreAreaWithTwoDecimalsAndBuildup()
    {
        var user = await _database.CreateUserAsync();
        var bauakte = Bauakte.Create(Guid.CreateVersion7(), "EFH Weber", user.Id);
        var screed = ScreedType.Create(Guid.CreateVersion7(), "Zementestrich Test", "CT-C25-F4", ScreedBinder.Cement);
        var room = Room.Create(Guid.CreateVersion7(), bauakte.Id, "Wohnen");
        room.Update(null, "Wohnen", "EG", 31.456m, null);
        room.ChangeBuildup(screed.Id, 65, 40, underfloorHeating: true);

        await _database.WithDbAsync(async db =>
        {
            db.AddRange(bauakte, screed, room);
            await db.SaveChangesAsync();
        });

        var stored = await _database.WithDbAsync(db => db.Set<Room>().SingleAsync(r => r.Id == room.Id));
        stored.AreaSquareMeters.ShouldBe(31.46m);
        stored.ThicknessMillimeters.ShouldBe(65);
        stored.UnderfloorHeating.ShouldBeTrue();
    }

    [Fact]
    public void Room_WithNegativeArea_IsRejected()
    {
        var room = Room.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Bad");
        Should.Throw<ArgumentOutOfRangeException>(() => room.Update(null, "Bad", null, -1m, null));
    }
}
