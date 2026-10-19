using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Setup.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Setup;

public enum SetupFailure
{
    AlreadyCompleted,
    InvalidCode,
}

internal sealed class SetupService(
    EstrichOneDbContext db,
    IUserAccounts accounts,
    IRoleAssignments roles,
    ICompanyProfiles company,
    IAuditLog audit,
    TimeProvider clock) : ISetupState
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromHours(24);

    /// <summary>Schlüssel der Advisory-Sperre, die parallele Einrichtungsversuche nacheinander ausführt.</summary>
    private const long SetupLockKey = 0x45_73_74_72_69_63_68; // „Estrich“

    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken) =>
        !await db.Set<User>().AnyAsync(cancellationToken);

    /// <summary>Erzeugt einen neuen Code, wenn die Einrichtung aussteht. Liefert den Klartext oder <c>null</c>.</summary>
    public async Task<string?> IssueCodeIfRequiredAsync(CancellationToken cancellationToken)
    {
        if (!await IsSetupRequiredAsync(cancellationToken))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var existing = await db.Set<SetupCode>().SingleOrDefaultAsync(cancellationToken);
        string code;
        if (existing is null)
        {
            (existing, code) = SetupCode.Issue(now, CodeLifetime);
            db.Add(existing);
        }
        else
        {
            code = SetupCode.Generate();
            existing.Replace(code, now + CodeLifetime);
        }

        await db.SaveChangesAsync(cancellationToken);
        return code;
    }

    /// <exception cref="PasswordRejectedException"/>
    /// <exception cref="DuplicateUserException"/>
    public async Task<(User? Administrator, SetupFailure? Failure)> CompleteAsync(string code, string companyName,
        string displayName, string userName, string? email, string password, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({SetupLockKey})", cancellationToken);
        if (!await IsSetupRequiredAsync(cancellationToken))
        {
            return (null, SetupFailure.AlreadyCompleted);
        }

        var stored = await db.Set<SetupCode>().SingleOrDefaultAsync(cancellationToken);
        if (stored is null || !stored.Matches(code, clock.GetUtcNow()))
        {
            if (stored is not null)
            {
                stored.RecordFailure();
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return (null, SetupFailure.InvalidCode);
        }

        await company.CreateAsync(companyName, cancellationToken);
        var administrator = await accounts.CreateLocalUserAsync(displayName, userName, email, password,
            mustChangePassword: false, cancellationToken);
        await roles.SetRolesAsync(administrator.Id, [roles.AdministratorRoleId], cancellationToken);
        db.Remove(stored);
        audit.Record("setup.completed", administrator.Id, "user", administrator.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (administrator, null);
    }
}

/// <summary>Gibt beim Start den Einrichtungscode aus, solange die Installation nicht eingerichtet ist.</summary>
internal sealed partial class SetupCodeAnnouncer(IServiceScopeFactory scopes, ILogger<SetupCodeAnnouncer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var code = await scope.ServiceProvider.GetRequiredService<SetupService>().IssueCodeIfRequiredAsync(cancellationToken);
            if (code is not null)
            {
                LogSetupCode(logger, code, (int)SetupService.CodeLifetime.TotalHours);
            }
        }
        catch (Npgsql.PostgresException exception) when (exception.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
        {
            // Datenbank noch nicht migriert – der Code wird nach dem nächsten Start ausgegeben.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "EstrichONE ist noch nicht eingerichtet. Einrichtungscode: {SetupCode:l} (gültig {Hours} Stunden, einmalig). " +
        "Ein Neustart erzeugt einen neuen Code.")]
    private static partial void LogSetupCode(ILogger logger, string setupCode, int hours);
}
