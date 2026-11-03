using System.Net;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Audit;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Identity;

/// <summary>Credential Stuffing: ein Zähler je Konto (Name und E-Mail gemeinsam), Alarm an die Benutzerverwaltung.</summary>
public sealed class BruteForceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
    {
        ["EstrichOne:Authentication:RequestsPerMinutePerAddress"] = "10000",
    });

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task FailuresViaNameAndEmail_ShareOneCounter_AndAlertAdministratorsOnce()
    {
        var admin = await _factory.CreateUserAsync("chefin", roleIds: StandardRoles.AdministratorId);
        await _factory.CreateUserAsync("m.huber");
        using var client = _factory.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            using var _ = await client.LoginAsync("m.huber", "Falsches-Passwort-1");
        }

        for (var i = 0; i < 2; i++)
        {
            using var _ = await client.LoginAsync("m.huber@example.org", "Falsches-Passwort-1");
        }

        using var locked = await client.LoginAsync("M.Huber@Example.org");
        locked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Weitere Versuche lösen keinen zweiten Alarm aus.
        using (var _ = await client.LoginAsync("m.huber", "Falsches-Passwort-1"))
        {
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var alerts = await db.Set<Notification>().Where(n => n.UserId == admin.Id && n.Title == "Anmeldeangriff erkannt").ToListAsync(Ct);
        alerts.ShouldHaveSingleItem().Body.ShouldNotBeNull().ShouldContain("m.huber");
        (await db.Set<AuditEvent>().CountAsync(a => a.Action == "auth.login.attack_detected", Ct)).ShouldBe(1);
    }
}
