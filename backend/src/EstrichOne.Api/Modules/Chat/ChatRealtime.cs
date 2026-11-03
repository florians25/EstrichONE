using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;

namespace EstrichOne.Api.Modules.Chat;

/// <summary>
/// Ereignis für offene Clients (Server-Sent Events). Enthält nur IDs, nie Inhalte – der Client lädt nach
/// (<c>GET …/messages?after=</c> bzw. <c>GET /chat/messages/{id}</c>).
/// </summary>
/// <param name="Type"><c>message</c>, <c>message.updated</c>, <c>message.deleted</c>, <c>reaction</c>, <c>read</c>, <c>typing</c>, <c>conversation</c>, <c>conversation.removed</c>.</param>
public sealed record ChatEvent(string Type, Guid ConversationId, Guid? MessageId = null, Guid? UserId = null, string? UserName = null);

/// <summary>
/// Verteilt Chat-Ereignisse an verbundene Clients dieses Servers (ein Prozess, kein Bus nötig). Je Verbindung ein
/// begrenzter Kanal; langsame Clients verlieren alte Ereignisse statt den Server zu bremsen (sie gleichen per Abruf ab).
/// </summary>
internal sealed class ChatHub
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<ChatEvent>>> _connections = new();

    public int ConnectionCount => _connections.Values.Sum(c => c.Count);

    public (Guid Id, ChannelReader<ChatEvent> Reader) Subscribe(Guid userId)
    {
        var channel = Channel.CreateBounded<ChatEvent>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var id = Guid.NewGuid();
        _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<ChatEvent>>())[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid userId, Guid connectionId)
    {
        if (_connections.TryGetValue(userId, out var list) && list.TryRemove(connectionId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public bool IsOnline(Guid userId) => _connections.TryGetValue(userId, out var list) && !list.IsEmpty;

    public void Publish(IEnumerable<Guid> userIds, ChatEvent chatEvent)
    {
        foreach (var userId in userIds.Distinct())
        {
            if (_connections.TryGetValue(userId, out var list))
            {
                foreach (var channel in list.Values)
                {
                    channel.Writer.TryWrite(chatEvent);
                }
            }
        }
    }
}

/// <summary>SSE-Strom <c>GET /chat/events</c>: hinter Caddy und Nginx Proxy Manager tauglich (kein Puffern, Herzschlag).</summary>
internal static class ChatEventStream
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Herzschlag unter den üblichen Proxy-Timeouts (Nginx: 60 s).</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    /// <summary>Verbindung nach dieser Zeit beenden – der Client verbindet neu (frisches Zugriffstoken, Rechteprüfung).</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(15);

    public static async Task RunAsync(HttpContext http, ChatHub hub, Guid userId, TimeProvider clock)
    {
        var response = http.Response;
        response.Headers.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache, no-store, no-transform"; // no-transform: Proxys komprimieren/puffern nicht
        response.Headers["X-Accel-Buffering"] = "no"; // Nginx (Proxy Manager): nicht puffern.
        response.Headers.Connection = "keep-alive";
        http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

        var (connection, reader) = hub.Subscribe(userId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        lifetime.CancelAfter(MaxDuration);
        var token = lifetime.Token;
        try
        {
            await response.WriteAsync($"retry: 3000\nevent: ready\ndata: {{\"type\":\"ready\",\"at\":\"{clock.GetUtcNow():O}\"}}\n\n", token);
            await response.Body.FlushAsync(token);
            while (!token.IsCancellationRequested)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                wait.CancelAfter(Heartbeat);
                try
                {
                    var item = await reader.ReadAsync(wait.Token);
                    await response.WriteAsync($"event: {item.Type}\ndata: {JsonSerializer.Serialize(item, Json)}\n\n", token);
                    while (reader.TryRead(out var more))
                    {
                        await response.WriteAsync($"event: {more.Type}\ndata: {JsonSerializer.Serialize(more, Json)}\n\n", token);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    await response.WriteAsync(": ping\n\n", token);
                }

                await response.Body.FlushAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
            // Client weg oder Höchstdauer erreicht.
        }
        catch (ChannelClosedException)
        {
            // Abgemeldet.
        }
        catch (IOException)
        {
            // Verbindung abgebrochen.
        }
        finally
        {
            hub.Unsubscribe(userId, connection);
        }
    }
}

/// <summary>
/// Anhänge und Gruppenbilder verschlüsselt im Dateispeicher (<c>{Files:RootPath}/chat/…</c>): ASP.NET Data Protection
/// (AES-256-GCM, Zweck <c>EstrichOne.Chat.Attachments.v1</c>), derselbe Schlüsselring wie die Nachrichtentexte.
/// </summary>
internal sealed class ChatBlobs(IConfiguration configuration, IDataProtectionProvider protection)
{
    public const string ProtectorPurpose = "EstrichOne.Chat.Attachments.v1";

    private string Root => Path.GetFullPath(configuration["EstrichOne:Files:RootPath"] ?? "/data/files");

    private IDataProtector Protector => protection.CreateProtector(ProtectorPurpose);

    public static string KeyFor(Guid id, DateTimeOffset at) => $"chat/{at:yyyy}/{at:MM}/{id:N}";

    private string PathOf(string key, string suffix)
    {
        var path = Path.GetFullPath(Path.Combine(Root, key + suffix));
        return path.StartsWith(Root, StringComparison.Ordinal) ? path : throw new InvalidOperationException("Ungültiger Speicherort.");
    }

    public async Task WriteAsync(string key, byte[] content, bool thumbnail, CancellationToken cancellationToken)
    {
        var path = PathOf(key, thumbnail ? ".thumb" : ".bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, Protector.Protect(content), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    public async Task<byte[]?> ReadAsync(string key, bool thumbnail, CancellationToken cancellationToken)
    {
        var path = PathOf(key, thumbnail ? ".thumb" : ".bin");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Protector.Unprotect(await File.ReadAllBytesAsync(path, cancellationToken));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null; // Schlüsselring fehlt (Wiederherstellung ohne .keys).
        }
    }

    public async Task CopyAsync(string from, string to, CancellationToken cancellationToken)
    {
        foreach (var suffix in new[] { ".bin", ".thumb" })
        {
            var source = PathOf(from, suffix);
            if (File.Exists(source))
            {
                var target = PathOf(to, suffix);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = File.OpenRead(source);
                await using var output = File.Create(target);
                await input.CopyToAsync(output, cancellationToken);
            }
        }
    }

    public void Delete(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            foreach (var suffix in new[] { ".bin", ".thumb" })
            {
                try
                {
                    File.Delete(PathOf(key, suffix));
                }
                catch (IOException)
                {
                    // Nächster Löschlauf versucht es erneut nicht – Datei ist dann verwaist, aber verschlüsselt.
                }
            }
        }
    }
}
