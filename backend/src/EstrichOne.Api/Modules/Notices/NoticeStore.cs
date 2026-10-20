using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Notices;

/// <summary>Hält die Systemhinweise im Speicher und – falls konfiguriert – in einer JSON-Datei (atomar geschrieben).</summary>
public sealed class NoticeStore(IOptions<NoticeSettings> options, TimeProvider clock, ILogger<NoticeStore> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _sync = new();
    private SystemNoticesDocument? _document;

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
    public SystemNoticesResponse ForClients()
    {
        var now = clock.GetUtcNow();
        var document = Current;
        var announcements = document.Announcements
            .Where(a => (a.StartsAt is null || a.StartsAt <= now) && (a.EndsAt is null || a.EndsAt > now))
            .ToList();
        MaintenanceState? maintenance = null;
        if (document.Maintenance is { } window && (window.EndsAt is null || window.EndsAt > now)
            && window.StartsAt - now <= options.Value.MaintenancePreview)
        {
            maintenance = new MaintenanceState(window.StartsAt <= now, window.Message, window.StartsAt, window.EndsAt, window.AdminsMayWork);
        }

        return new SystemNoticesResponse(announcements, maintenance, now);
    }

    /// <summary>Läuft gerade eine Wartung?</summary>
    public MaintenanceWindow? ActiveMaintenance()
    {
        var now = clock.GetUtcNow();
        return Current.Maintenance is { } window && window.StartsAt <= now && (window.EndsAt is null || window.EndsAt > now) ? window : null;
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
