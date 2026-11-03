using EstrichOne.Hosting.Data;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Infrastructure;

/// <summary>In der Konsole änderbare Einstellungen (überschreiben die Vorgaben aus der Umgebung).</summary>
public sealed class ConsoleSettings(HostingDbContext db, IOptions<HostingOptions> options)
{
    public const string DefaultVersionKey = "defaultVersion";

    /// <summary>Zweites Sicherungsziel für alle Instanzen (restic-Repository, je Instanz ein Unterpfad <c>/&lt;kürzel&gt;</c>).</summary>
    public const string SecondaryRepositoryKey = "secondaryRepository";

    /// <summary>Einrichtungsassistent der Konsole abgeschlossen.</summary>
    public const string WizardCompletedKey = "setupWizardCompleted";

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken) =>
        (await db.Settings.FindAsync([key], cancellationToken))?.Value;

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
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

        await db.SaveChangesAsync(cancellationToken);
    }

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
