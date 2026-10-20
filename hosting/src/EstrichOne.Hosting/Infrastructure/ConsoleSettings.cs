using EstrichOne.Hosting.Data;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Infrastructure;

/// <summary>In der Konsole änderbare Einstellungen (überschreiben die Vorgaben aus der Umgebung).</summary>
public sealed class ConsoleSettings(HostingDbContext db, IOptions<HostingOptions> options)
{
    public const string DefaultVersionKey = "defaultVersion";

    public async Task<string> GetDefaultVersionAsync(CancellationToken cancellationToken) =>
        (await db.Settings.FindAsync([DefaultVersionKey], cancellationToken))?.Value ?? options.Value.DefaultVersion;

    public async Task SetDefaultVersionAsync(string version, CancellationToken cancellationToken)
    {
        var setting = await db.Settings.FindAsync([DefaultVersionKey], cancellationToken);
        if (setting is null)
        {
            db.Settings.Add(new Setting { Key = DefaultVersionKey, Value = version });
        }
        else
        {
            setting.Value = version;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
