using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Backup.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Archive;

public enum MonitorLevel
{
    Ok,
    Warning,
    Critical,
}

/// <summary>Stündlicher Messpunkt der Plattenbelegung je System (für den Trend).</summary>
public sealed class MonitoringSample
{
    public long Id { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public DateTimeOffset At { get; private set; }
    public long UsedBytes { get; private set; }
    public long TotalBytes { get; private set; }

    public static MonitoringSample Create(string source, DateTimeOffset at, long used, long total) =>
        new() { Source = source, At = at, UsedBytes = used, TotalBytes = total };
}

internal sealed class MonitoringSampleConfiguration : IEntityTypeConfiguration<MonitoringSample>
{
    public void Configure(EntityTypeBuilder<MonitoringSample> builder)
    {
        builder.ToTable("monitoring_samples");
        builder.Property(s => s.Source).HasMaxLength(200);
        builder.HasIndex(s => new { s.Source, s.At });
    }
}

public sealed record DiskInfo(long UsedBytes, long FreeBytes, long TotalBytes, double UsedPercent, long? GrowthBytesPerDay, int? DaysUntilFull);

public sealed record MonitorItem(string Label, string Value, MonitorLevel Level);

public sealed record MonitoredSystem(string Key, string Name, MonitorLevel Level, DiskInfo? Disk, IReadOnlyList<MonitorItem> Items);

public sealed record MonitoringOverview(MonitorLevel Level, DateTimeOffset At, IReadOnlyList<MonitoredSystem> Systems, IReadOnlyList<string> Warnings);

/// <summary>Überwachung von Produktivsystem, Archivserver und Backup-Zielen (Platten, Dienste, letzte Läufe, Warnschwellen).</summary>
internal sealed class MonitoringService(
    EstrichOneDbContext db, ArchiveService archive, IBackupMonitoring backup, IConfiguration configuration, TimeProvider clock)
{
    public const double WarnPercent = 85;
    public const double CriticalPercent = 95;

    public async Task<MonitoringOverview> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var systems = new List<MonitoredSystem>();

        // Produktivsystem
        var items = new List<MonitorItem>();
        long? dbBytes = null;
        try
        {
            dbBytes = await db.Database.SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"").SingleAsync(cancellationToken);
            items.Add(new("Datenbank", $"in Betrieb · {Size(dbBytes.Value)}", MonitorLevel.Ok));
        }
        catch (Exception exception) when (exception is InvalidOperationException or Npgsql.NpgsqlException)
        {
            items.Add(new("Datenbank", "nicht erreichbar", MonitorLevel.Critical));
        }

        var b = await backup.GetAsync(cancellationToken);
        items.Add(new("Sicherung", b.Configured ? $"{b.Summary}{(b.LastSuccessAt is { } at ? $" (zuletzt {Local(at)})" : string.Empty)}" : "nicht eingerichtet",
            !b.Configured ? MonitorLevel.Warning : b.Healthy ? MonitorLevel.Ok : MonitorLevel.Critical));
        var queue = await db.Set<ArchiveRecord>().CountAsync(r => r.Status == ArchiveRecordStatus.Failed, cancellationToken);
        items.Add(new("Archiv-Warteschlange", queue == 0 ? "keine Fehler" : $"{queue} fehlgeschlagen", queue == 0 ? MonitorLevel.Ok : MonitorLevel.Warning));
        var filesRoot = configuration["EstrichOne:Files:RootPath"] ?? "/data/files";
        systems.Add(await SystemAsync("production", "Produktivsystem (EstrichONE-Server)", Drive(filesRoot), items, now, cancellationToken));

        // Archivserver
        var connection = await archive.ConnectionAsync(cancellationToken);
        if (connection is not null)
        {
            var status = await archive.ServerStatusAsync(connection, cancellationToken);
            var archiveItems = new List<MonitorItem>
            {
                new("Erreichbar", status is null ? $"nein – {connection.LastError}" : $"ja · Version {status.Version}",
                    status is null ? (now - (connection.LastContactAt ?? now) > TimeSpan.FromHours(24) ? MonitorLevel.Critical : MonitorLevel.Warning) : MonitorLevel.Ok),
            };
            if (status?.Archive is { } stats)
            {
                archiveItems.Add(new("Bestand", $"{stats.Objects} Dokumente · {Size(stats.Bytes)} · Journal {stats.LedgerEntries}", MonitorLevel.Ok));
            }

            if (status is not null)
            {
                var v = status.LastVerification;
                archiveItems.Add(new("Letzte Prüfung", v is null ? "noch keine" : $"{(v.Ok ? "in Ordnung" : $"{v.Errors.Count} Abweichung(en)")} · {Local(v.At)}",
                    v is null ? MonitorLevel.Warning : !v.Ok ? MonitorLevel.Critical : now - v.At > TimeSpan.FromDays(2) ? MonitorLevel.Warning : MonitorLevel.Ok));
            }

            var lastArchived = await db.Set<ArchiveRecord>().Where(r => r.ArchivedAt != null).MaxAsync(r => r.ArchivedAt, cancellationToken);
            archiveItems.Add(new("Letzte Archivierung", lastArchived is null ? "noch keine" : Local(lastArchived.Value), MonitorLevel.Ok));
            var disk = status?.DiskTotalBytes is { } total && status.DiskFreeBytes is { } free ? (total - free, total) : ((long, long)?)null;
            systems.Add(await SystemAsync("archive", $"Archivserver „{connection.ServerName}“", disk, archiveItems, now, cancellationToken));
        }
        else
        {
            systems.Add(new("archive", "Archivserver", MonitorLevel.Warning, null, [new("Status", "nicht gekoppelt (System → Archiv)", MonitorLevel.Warning)]));
        }

        // Backup-Ziele
        foreach (var target in b.Targets)
        {
            var targetItems = new List<MonitorItem>
            {
                new("Erreichbar", target.Reachable ? "ja" : $"nein{(target.Error is null ? string.Empty : $" – {target.Error}")}",
                    target.Reachable ? MonitorLevel.Ok : MonitorLevel.Warning),
                new("Letzte Sicherung dorthin", target.LastRunAt is null ? "noch keine" : $"{(target.LastRunStatus == "succeeded" ? "erfolgreich" : "fehlgeschlagen")} · {Local(target.LastRunAt.Value)}",
                    target.LastRunAt is null ? MonitorLevel.Warning : target.LastRunStatus != "succeeded" || now - target.LastRunAt > TimeSpan.FromHours(36)
                        ? MonitorLevel.Critical : MonitorLevel.Ok),
            };
            if (target.RepositoryBytes is { } repo)
            {
                targetItems.Add(new("Sicherungen", Size(repo), MonitorLevel.Ok));
            }

            var disk = target.DiskTotalBytes is { } total && target.DiskFreeBytes is { } free ? (total - free, total) : ((long, long)?)null;
            systems.Add(await SystemAsync($"backup:{target.Address}", $"Backup-Ziel „{target.Name}“", disk, targetItems, now, cancellationToken));
        }

        if (b.Targets.Count == 0)
        {
            systems.Add(new("backup", "Backup-Ziel", MonitorLevel.Warning, null,
                [new("Status", "keines gekoppelt – Sicherung nur auf diesem Server (System → Backup-Ziele)", MonitorLevel.Warning)]));
        }

        await db.SaveChangesAsync(cancellationToken);
        var warnings = systems.SelectMany(s => s.Items.Where(i => i.Level != MonitorLevel.Ok).Select(i => $"{s.Name}: {i.Label} – {i.Value}"))
            .Concat(systems.Where(s => s.Disk is { UsedPercent: >= WarnPercent }).Select(s => $"{s.Name}: Platte zu {s.Disk!.UsedPercent:0} % belegt"))
            .ToList();
        return new MonitoringOverview(systems.Count == 0 ? MonitorLevel.Ok : systems.Max(s => s.Level), now, systems, warnings);
    }

    private async Task<MonitoredSystem> SystemAsync(string key, string name, (long Used, long Total)? disk, List<MonitorItem> items,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        DiskInfo? info = null;
        if (disk is { Total: > 0 } d)
        {
            var samples = db.Set<MonitoringSample>();
            var last = await samples.Where(s => s.Source == key).OrderByDescending(s => s.At).Select(s => (DateTimeOffset?)s.At)
                .FirstOrDefaultAsync(cancellationToken);
            if (last is null || now - last >= TimeSpan.FromHours(1))
            {
                samples.Add(MonitoringSample.Create(key, now, d.Used, d.Total));
            }

            var oldest = await samples.Where(s => s.Source == key && s.At >= now.AddDays(-7)).OrderBy(s => s.At).FirstOrDefaultAsync(cancellationToken);
            long? perDay = null;
            int? daysUntilFull = null;
            if (oldest is not null && now - oldest.At >= TimeSpan.FromHours(6))
            {
                perDay = (long)((d.Used - oldest.UsedBytes) / (now - oldest.At).TotalDays);
                daysUntilFull = perDay > 0 ? (int)Math.Min(36500, (d.Total - d.Used) / perDay.Value) : null;
            }

            var percent = Math.Round(100.0 * d.Used / d.Total, 1);
            info = new DiskInfo(d.Used, d.Total - d.Used, d.Total, percent, perDay, daysUntilFull);
            var level = percent >= CriticalPercent || daysUntilFull < 14 ? MonitorLevel.Critical
                : percent >= WarnPercent || daysUntilFull < 60 ? MonitorLevel.Warning : MonitorLevel.Ok;
            items.Insert(0, new("Platte", $"{Size(d.Used)} von {Size(d.Total)} belegt ({percent:0.#} %), {Size(d.Total - d.Used)} frei"
                + (perDay is { } g ? $" · Trend {(g >= 0 ? "+" : "")}{Size(g)}/Tag" : string.Empty)
                + (daysUntilFull is { } days ? $" · voll in ca. {days} Tagen" : string.Empty), level));
        }

        return new MonitoredSystem(key, name, items.Count == 0 ? MonitorLevel.Ok : items.Max(i => i.Level), info, items);
    }

    private static (long Used, long Total)? Drive(string path)
    {
        try
        {
            var drive = new DriveInfo(Path.GetFullPath(path));
            return (drive.TotalSize - drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Local(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    internal static string Size(long bytes)
    {
        var abs = Math.Abs((double)bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (abs >= 1024 && i < units.Length - 1)
        {
            abs /= 1024;
            i++;
        }

        return $"{(bytes < 0 ? "-" : string.Empty)}{abs.ToString(i == 0 ? "0" : "0.#", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))} {units[i]}";
    }
}

internal static class MonitoringEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var monitoring = api.MapGroup("/system/monitoring").WithTags("Überwachung");
        monitoring.MapGet("/", async (MonitoringService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(ct)))
            .WithName("GetMonitoring").WithSummary("Produktivsystem, Archivserver und Backup-Ziele: Platten (Belegung, Trend), Dienste, letzte Läufe")
            .RequirePermission(Permissions.BackupManage);
    }
}
