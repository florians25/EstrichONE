using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Hosting.Data;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Hosting.Auth;

/// <summary>
/// Ersteinrichtung wie in der Suite: Solange es keinen Administrator gibt, schreibt die Konsole beim Start einen
/// Einmal-Code ins Protokoll (24 Stunden gültig; jeder Neustart erzeugt einen neuen). Gespeichert wird nur der Hash.
/// </summary>
public sealed partial class SetupCodeService(HostingDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private const string HashKey = "setup.codeHash";
    private const string ExpiryKey = "setup.codeExpires";
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken) => !await db.Admins.AnyAsync(cancellationToken);

    public async Task<string?> IssueIfRequiredAsync(CancellationToken cancellationToken)
    {
        if (await db.Admins.AnyAsync(cancellationToken))
        {
            return null;
        }

        var chars = RandomNumberGenerator.GetString(Alphabet, 12);
        var code = $"{chars[..4]}-{chars[4..8]}-{chars[8..]}";
        await SetAsync(HashKey, Hash(code), cancellationToken);
        await SetAsync(ExpiryKey, clock.GetUtcNow().Add(Lifetime).ToString("O", CultureInfo.InvariantCulture), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return code;
    }

    public async Task<bool> VerifyAsync(string? code, CancellationToken cancellationToken)
    {
        var hash = await db.Settings.FindAsync([HashKey], cancellationToken);
        var expiry = await db.Settings.FindAsync([ExpiryKey], cancellationToken);
        if (hash is null || expiry is null || string.IsNullOrWhiteSpace(code)
            || !DateTimeOffset.TryParse(expiry.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expires)
            || clock.GetUtcNow() > expires)
        {
            return false;
        }

        var normalized = code.Trim().ToUpperInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(normalized)), Encoding.ASCII.GetBytes(hash.Value));
    }

    public async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await db.Settings.Where(s => s.Key == HashKey || s.Key == ExpiryKey).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        var setting = await db.Settings.FindAsync([key], cancellationToken);
        if (setting is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
    }

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}

/// <summary>Gibt beim Start den Einrichtungscode aus, solange kein Administrator existiert.</summary>
public sealed partial class SetupCodeAnnouncer(IServiceScopeFactory scopes, ILogger<SetupCodeAnnouncer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var code = await scope.ServiceProvider.GetRequiredService<SetupCodeService>().IssueIfRequiredAsync(cancellationToken);
        if (code is not null)
        {
            LogSetupCode(logger, code);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "Die Hosting-Konsole ist noch nicht eingerichtet. Einrichtungscode: {SetupCode} (gültig 24 Stunden, einmalig). Ein Neustart erzeugt einen neuen Code.")]
    private static partial void LogSetupCode(ILogger logger, string setupCode);
}
