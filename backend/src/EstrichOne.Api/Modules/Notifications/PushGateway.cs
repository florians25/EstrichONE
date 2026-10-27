using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Notifications;

/// <summary>APNs-Vorgaben aus der Umgebung (<c>.env</c>); Werte aus den Einstellungen haben Vorrang.</summary>
public sealed class PushOptions
{
    public const string SectionName = "EstrichOne:Push";

    /// <summary>Kontakt für die Push-Dienste (VAPID „sub“), z. B. <c>mailto:it@firma.de</c>.</summary>
    public string? VapidSubject { get; set; }

    public string? ApnsKeyId { get; set; }
    public string? ApnsTeamId { get; set; }
    public string? ApnsBundleId { get; set; }

    /// <summary>Pfad zur .p8-Datei (z. B. als Docker-Secret eingebunden).</summary>
    public string? ApnsKeyPath { get; set; }

    public bool? ApnsProduction { get; set; }

    /// <summary>Zusätzlich erlaubte Hosts für Web-Push-Endpunkte (Standard: die Dienste von Google, Mozilla, Apple, Microsoft).</summary>
    public List<string> WebPushHosts { get; set; } = [];
}

/// <summary>Wirksame Push-Einrichtung (Einstellungen + Umgebung), mit entschlüsselten Schlüsseln nur im Speicher.</summary>
internal sealed record PushConfig(
    bool WebPushEnabled, string? VapidPublicKey, string? VapidPrivateKey, string VapidSubject,
    bool ApnsReady, string? ApnsKeyId, string? ApnsTeamId, string? ApnsBundleId, bool ApnsProduction, string? ApnsKey, bool ApnsFromEnvironment,
    bool ShowContent);

internal sealed class PushKeys(EstrichOneDbContext db, IDataProtectionProvider protection, IOptions<PushOptions> options,
    IOptions<EstrichOneOptions> app)
{
    public const string ProtectorPurpose = "EstrichOne.Push.Keys";

    private IDataProtector Protector => protection.CreateProtector(ProtectorPurpose);

    public string Protect(string secret) => Protector.Protect(secret);

    /// <summary>Einstellungszeile holen; VAPID-Schlüssel beim ersten Bedarf automatisch erzeugen.</summary>
    public async Task<PushSettingsRow> RowAsync(CancellationToken cancellationToken)
    {
        var row = await db.Set<PushSettingsRow>().SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new PushSettingsRow();
            db.Add(row);
        }

        if (row.VapidPublicKey is null || row.ProtectedVapidPrivateKey is null)
        {
            var (publicKey, privateKey) = PushCrypto.CreateVapidKeys();
            row.VapidPublicKey = publicKey;
            row.ProtectedVapidPrivateKey = Protect(privateKey);
        }

        if (db.ChangeTracker.HasChanges())
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Gleichzeitig angelegt: die andere Zeile gilt.
                db.ChangeTracker.Clear();
                row = await db.Set<PushSettingsRow>().SingleAsync(cancellationToken);
            }
        }

        return row;
    }

    public async Task<PushConfig> ConfigAsync(CancellationToken cancellationToken)
    {
        var row = await RowAsync(cancellationToken);
        var env = options.Value;
        string? vapidPrivate = null;
        try
        {
            vapidPrivate = Protector.Unprotect(row.ProtectedVapidPrivateKey!);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Schlüsselring verloren (Wiederherstellung ohne .keys): neue Schlüssel erzeugen, Browser melden sich neu an.
            row.VapidPublicKey = null;
            row.ProtectedVapidPrivateKey = null;
            row = await RowAsync(cancellationToken);
            vapidPrivate = Protector.Unprotect(row.ProtectedVapidPrivateKey!);
        }

        string? apnsKey = null;
        var fromEnvironment = false;
        if (row.ProtectedApnsKey is not null)
        {
            try
            {
                apnsKey = Protector.Unprotect(row.ProtectedApnsKey);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                apnsKey = null;
            }
        }
        else if (!string.IsNullOrWhiteSpace(env.ApnsKeyPath) && File.Exists(env.ApnsKeyPath))
        {
            apnsKey = await File.ReadAllTextAsync(env.ApnsKeyPath, cancellationToken);
            fromEnvironment = true;
        }

        var keyId = row.ApnsKeyId ?? Value(env.ApnsKeyId);
        var teamId = row.ApnsTeamId ?? Value(env.ApnsTeamId);
        var bundleId = row.ApnsBundleId ?? Value(env.ApnsBundleId);
        var production = row.ProtectedApnsKey is null && env.ApnsProduction is { } p ? p : row.ApnsProduction;
        var enabled = row.ApnsEnabled || (fromEnvironment && row.ProtectedApnsKey is null);
        var ready = enabled && apnsKey is not null && !string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(teamId)
            && !string.IsNullOrWhiteSpace(bundleId);
        var subject = row.VapidSubject ?? Value(env.VapidSubject) ?? (app.Value.PublicUrl?.ToString().TrimEnd('/') ?? "mailto:admin@localhost");
        return new PushConfig(row.WebPushEnabled, row.VapidPublicKey, vapidPrivate, subject, ready, keyId, teamId, bundleId, production, apnsKey,
            fromEnvironment, row.ShowContent);
    }

    private static string? Value(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Web-Push-Endpunkte nur bei bekannten Push-Diensten (sonst könnte ein Abo den Server beliebige Adressen aufrufen lassen).</summary>
    public bool IsAllowedWebPushEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        string[] defaults = ["fcm.googleapis.com", "push.services.mozilla.com", "push.apple.com", "notify.windows.com", "push.microsoft.com"];
        return defaults.Concat(options.Value.WebPushHosts).Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Ergebnis eines Zustellversuchs an ein Gerät.</summary>
internal enum PushResult
{
    Delivered,

    /// <summary>Token/Abo ungültig oder abgelaufen – Gerät entfernen.</summary>
    Gone,

    Failed,

    /// <summary>Kanal nicht eingerichtet (APNs ohne Schlüssel, Android ohne FCM).</summary>
    NotConfigured,
}

/// <summary>Inhalt einer Pushnachricht. Bewusst knapp: Titel, kurzer Text, Link, IDs – nie Anhänge oder Kommentare.</summary>
internal sealed record PushMessage(Guid NotificationId, string Title, string? Body, string? Link, string Category, string Urgency, int Badge);

/// <summary>Versand an Web-Push-Dienste und APNs (HTTP/2, Token-Anmeldung).</summary>
internal sealed class PushGateway(IHttpClientFactory http, TimeProvider clock)
{
    public const string HttpClientName = "push";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Lock ApnsLock = new();
    private static (string Key, string Token, DateTimeOffset At)? _apnsToken;

    public async Task<(PushResult Result, string? Error)> SendAsync(PushDevice device, PushMessage message, PushConfig config, CancellationToken cancellationToken)
    {
        try
        {
            return device.Platform switch
            {
                PushPlatform.Web => await SendWebAsync(device, message, config, cancellationToken),
                PushPlatform.Ios => await SendApnsAsync(device, message, config, cancellationToken),
                _ => (PushResult.NotConfigured, "Android-Push (FCM) ist noch nicht eingerichtet – nur Postfach."),
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Security.Cryptography.CryptographicException
            or FormatException or ArgumentException)
        {
            return (PushResult.Failed, exception.Message);
        }
    }

    private async Task<(PushResult, string?)> SendWebAsync(PushDevice device, PushMessage message, PushConfig config, CancellationToken cancellationToken)
    {
        if (!config.WebPushEnabled || config.VapidPrivateKey is null || device.P256dh is null || device.AuthSecret is null)
        {
            return (PushResult.NotConfigured, "Web-Push ist ausgeschaltet.");
        }

        var endpoint = new Uri(device.Token);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = message.NotificationId, title = message.Title, body = message.Body, link = message.Link, category = message.Category, badge = message.Badge,
        }, Json);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(PushCrypto.EncryptWebPush(payload, device.P256dh, device.AuthSecret)),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("Authorization",
            PushCrypto.VapidAuthorization(endpoint, config.VapidPublicKey!, config.VapidPrivateKey, config.VapidSubject, clock.GetUtcNow()));
        request.Headers.TryAddWithoutValidation("TTL", "86400");
        request.Headers.TryAddWithoutValidation("Urgency", message.Urgency == "high" ? "high" : message.Urgency == "low" ? "low" : "normal");
        request.Headers.TryAddWithoutValidation("Topic", message.Category);

        using var response = await http.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Accepted => (PushResult.Delivered, null),
            HttpStatusCode.NotFound or HttpStatusCode.Gone => (PushResult.Gone, "Abo abgelaufen"),
            _ => (PushResult.Failed, $"Push-Dienst antwortet {(int)response.StatusCode}"),
        };
    }

    private async Task<(PushResult, string?)> SendApnsAsync(PushDevice device, PushMessage message, PushConfig config, CancellationToken cancellationToken)
    {
        if (!config.ApnsReady)
        {
            return (PushResult.NotConfigured, "APNs ist nicht eingerichtet – nur Postfach.");
        }

        var sandbox = device.Sandbox || !config.ApnsProduction;
        var host = sandbox ? "https://api.sandbox.push.apple.com" : "https://api.push.apple.com";
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["aps"] = new Dictionary<string, object?>
            {
                ["alert"] = new Dictionary<string, object?> { ["title"] = message.Title, ["body"] = message.Body },
                ["sound"] = "default",
                ["badge"] = message.Badge,
                ["thread-id"] = message.Category,
                ["mutable-content"] = 1,
            },
            ["notificationId"] = message.NotificationId,
            ["link"] = message.Link,
            ["category"] = message.Category,
        }, Json);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/3/device/{Uri.EscapeDataString(device.Token)}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("authorization", $"bearer {ApnsToken(config)}");
        request.Headers.TryAddWithoutValidation("apns-topic", device.BundleId ?? config.ApnsBundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", message.Urgency == "low" ? "5" : "10");
        request.Headers.TryAddWithoutValidation("apns-expiration", clock.GetUtcNow().AddDays(1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("apns-collapse-id", message.NotificationId.ToString("N")[..32]);

        using var response = await http.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return (PushResult.Delivered, null);
        }

        var reason = await ReasonAsync(response, cancellationToken);
        return response.StatusCode == HttpStatusCode.Gone || reason is "BadDeviceToken" or "Unregistered" or "DeviceTokenNotForTopic"
            ? (PushResult.Gone, reason)
            : (PushResult.Failed, $"APNs {(int)response.StatusCode} {reason}".Trim());
    }

    private string ApnsToken(PushConfig config)
    {
        var now = clock.GetUtcNow();
        var cacheKey = $"{config.ApnsKeyId}:{config.ApnsTeamId}";
        lock (ApnsLock)
        {
            if (_apnsToken is { } cached && cached.Key == cacheKey && now - cached.At < TimeSpan.FromMinutes(45))
            {
                return cached.Token;
            }

            var token = PushCrypto.ApnsToken(config.ApnsKey!, config.ApnsKeyId!, config.ApnsTeamId!, now);
            _apnsToken = (cacheKey, token, now);
            return token;
        }
    }

    private static async Task<string?> ReasonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return doc.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Hash(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
