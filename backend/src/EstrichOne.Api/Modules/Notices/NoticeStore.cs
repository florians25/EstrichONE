using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Notices;

/// <summary>Hält die Systemhinweise im Speicher und – falls konfiguriert – in einer JSON-Datei (atomar geschrieben).</summary>
/// Dazu kommen die Hinweise der Instanz-Administratoren (Datenbank, zwischengespeichert); Clients sehen beide zusammengeführt.
public sealed class NoticeStore(IOptions<NoticeSettings> options, TimeProvider clock, ILogger<NoticeStore> logger, IServiceScopeFactory? scopes = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _sync = new();
    private SystemNoticesDocument? _document;
    private SystemNoticesDocument? _instance;

    /// <summary>Hinweise der Instanz-Administratoren (leer ohne Datenbank, z. B. in Unit-Tests).</summary>
    public SystemNoticesDocument Instance
    {
        get
        {
            lock (_sync)
            {
                return _instance ??= LoadInstance();
            }
        }
    }

    /// <summary>Instanz-Hinweise speichern (Datenbank) und den Zwischenspeicher aktualisieren.</summary>
    public async Task ReplaceInstanceAsync(SystemNoticesDocument document, CancellationToken cancellationToken)
    {
        if (scopes is not null)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            var row = await db.Set<InstanceNoticesRow>().SingleOrDefaultAsync(r => r.Id == InstanceNoticesRow.SingletonId, cancellationToken);
            if (row is null)
            {
                row = new InstanceNoticesRow();
                db.Add(row);
            }

            row.Update(JsonSerializer.Serialize(document, Json));
            await db.SaveChangesAsync(cancellationToken);
        }

        lock (_sync)
        {
            _instance = document;
        }
    }

    private SystemNoticesDocument LoadInstance()
    {
        var empty = new SystemNoticesDocument([], null);
        if (scopes is null)
        {
            return empty;
        }

        try
        {
            using var scope = scopes.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<InstanceNoticesRow>().AsNoTracking()
                .Where(r => r.Id == InstanceNoticesRow.SingletonId).Select(r => r.Data).SingleOrDefault();
            return data is null ? empty : JsonSerializer.Deserialize<SystemNoticesDocument>(data, Json) ?? empty;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or Npgsql.NpgsqlException)
        {
            // Vor der Migration (Tabelle fehlt) oder bei Datenbankfehlern: ohne Instanz-Hinweise weiter.
            logger.LogWarning(exception, "Instanz-Hinweise nicht lesbar.");
            return empty;
        }
    }

    private string? StoragePath => string.IsNullOrWhiteSpace(options.Value.StoragePath) ? null : options.Value.StoragePath;

    public SystemNoticesDocument Current
    {
        get
        {
            lock (_sync)
            {
                return _document ??= Load();
            }
        }
    }

    public void Replace(SystemNoticesDocument document)
    {
        lock (_sync)
        {
            if (StoragePath is { } path)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(document, Json));
                File.Move(temp, path, overwrite: true);
            }

            _document = document;
        }
    }

    /// <summary>Für Clients: nur gültige Ankündigungen; Wartung, wenn sie läuft oder bald beginnt.</summary>
    /// <param name="roleIds">Rollen des angemeldeten Benutzers; ohne Anmeldung nur Hinweise an alle.</param>
    public SystemNoticesResponse ForClients(IReadOnlyCollection<Guid>? roleIds = null)
    {
        var now = clock.GetUtcNow();
        var announcements = Current.Announcements.Select(a => a with { Source = "console" })
            .Concat(Instance.Announcements.Select(a => a with { Source = "instance" }))
            .Where(a => (a.StartsAt is null || a.StartsAt <= now) && (a.EndsAt is null || a.EndsAt > now))
            .Where(a => a.RoleIds is not { Count: > 0 } || (roleIds is not null && a.RoleIds.Any(roleIds.Contains)))
            .ToList();
        MaintenanceState? maintenance = null;
        // Die Wartung der Konsole (Betreiber) hat Vorrang vor der der Instanz.
        foreach (var (window, source) in new[] { (Current.Maintenance, "console"), (Instance.Maintenance, "instance") })
        {
            if (maintenance is null && window is not null && (window.EndsAt is null || window.EndsAt > now)
                && window.StartsAt - now <= options.Value.MaintenancePreview)
            {
                maintenance = new MaintenanceState(window.StartsAt <= now, window.Message, window.StartsAt, window.EndsAt, window.AdminsMayWork, source);
            }
        }

        return new SystemNoticesResponse(announcements, maintenance, now);
    }

    /// <summary>Läuft gerade eine Wartung (Konsole oder Instanz)?</summary>
    public MaintenanceWindow? ActiveMaintenance()
    {
        var now = clock.GetUtcNow();
        bool Running(MaintenanceWindow? w) => w is not null && w.StartsAt <= now && (w.EndsAt is null || w.EndsAt > now);
        return Running(Current.Maintenance) ? Current.Maintenance : Running(Instance.Maintenance) ? Instance.Maintenance : null;
    }

    private SystemNoticesDocument Load()
    {
        var empty = new SystemNoticesDocument([], null);
        if (StoragePath is not { } path || !File.Exists(path))
        {
            return empty;
        }

        try
        {
            return JsonSerializer.Deserialize<SystemNoticesDocument>(File.ReadAllText(path), Json) ?? empty;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning(exception, "Systemhinweise in {Path} nicht lesbar – leerer Stand.", path);
            return empty;
        }
    }
}
