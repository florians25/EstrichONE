// Integrationstests laufen unter Linux (CI); kurze Aufrufe ohne Abbruch-Token.
#pragma warning disable xUnit1051, CA1416
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EstrichOne.Api.Tests.Support;

/// <summary>
/// Nachgebildeter Microsoft-Graph- und Entra-ID-Server (kein echter Tenant): Anmeldung (Clientschlüssel, Zertifikat,
/// Gerätecode, Aktualisierungstoken), Benutzer, Teams, Kanäle, Gruppenchats, Mitglieder, Nachrichten und
/// Workflows-Webhooks. Zeichnet jede Anfrage auf; Drosselung (429 mit Retry-After) und fehlende Rechte (403) sind steuerbar.
/// </summary>
public sealed class FakeGraphServer : IAsyncDisposable
{
    public const string Secret = "richtiger-schluessel";
    public const string TeamId = "a1b2c3d4-0000-4000-8000-00000000babe";

    private readonly WebApplication _app;
    private int _devicePolls;
    private int _refreshCount;

    private FakeGraphServer(WebApplication app, int port)
    {
        _app = app;
        Url = $"http://127.0.0.1:{port}";
    }

    public string Url { get; }

    public sealed record GraphCall(string Method, string Path, string Query, string Body, string? Authorization);

    public ConcurrentQueue<GraphCall> Calls { get; } = new();

    /// <summary>Teilanfragen aus Graph-<c>$batch</c>-Aufrufen.</summary>
    public ConcurrentQueue<GraphCall> BatchCalls { get; } = new();

    /// <summary>Bekannte Konten: E-Mail/UPN → (ID, Name).</summary>
    public ConcurrentDictionary<string, (string Id, string Name)> Users { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Mitglieder je Chat bzw. Team: Konto-ID → Mitgliedschafts-ID.</summary>
    public ConcurrentDictionary<string, ConcurrentDictionary<string, string>> Members { get; } = new();

    /// <summary>Nächste N Aufrufe auf Pfade mit diesem Anfang mit 429 und Retry-After: 1 beantworten.</summary>
    public ConcurrentDictionary<string, int> Throttle { get; } = new();

    /// <summary>Pfade mit diesem Anfang: 403 (fehlende Berechtigung).</summary>
    public ConcurrentBag<string> Forbidden { get; } = [];

    /// <summary>Pfade mit diesem Anfang antworten erst nach dieser Wartezeit (überlastetes Microsoft 365, hängender Proxy).</summary>
    public ConcurrentDictionary<string, TimeSpan> Slow { get; } = new();

    /// <summary>Pfade mit diesem Anfang antworten mit festem Status und Text (z. B. HTML einer Proxy-Fehlerseite).</summary>
    public ConcurrentDictionary<string, (int Status, string Text)> Raw { get; } = new();

    /// <summary>Gewährte Anwendungsberechtigungen (Anspruch <c>roles</c> im App-Token); Standard: alle, die EstrichONE kennt.</summary>
    public ConcurrentDictionary<string, bool> AppRoles { get; } = new(new[]
    {
        "User.Read.All", "Team.ReadBasic.All", "Channel.Create", "TeamMember.ReadWrite.All", "Chat.Create", "ChatMember.ReadWrite.All",
        "ChannelSettings.ReadWrite.All", "Channel.Delete.All", "Chat.ManageDeletion.All",
    }.ToDictionary(r => r, _ => true), StringComparer.OrdinalIgnoreCase);

    /// <summary>Angelegte Kanäle: Kanal-ID → archiviert.</summary>
    public ConcurrentDictionary<string, bool> Channels { get; } = new();

    /// <summary>Gelöschte Kanäle bzw. Chats.</summary>
    public ConcurrentBag<string> Deleted { get; } = [];

    /// <summary>Angelegte Chats: Chat-ID → Art (<c>group</c>, <c>oneOnOne</c>).</summary>
    public ConcurrentDictionary<string, string> Chats { get; } = new();

    private string AppToken()
    {
        var payload = new JsonObject { ["aud"] = "https://graph.microsoft.com", ["roles"] = new JsonArray(AppRoles.Keys.Select(r => (JsonNode)r).ToArray()) };
        return "eyJhbGciOiJub25lIn0." + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_')
            + ".app";
    }

    private static bool IsAppToken(string authorization) =>
        authorization.StartsWith("Bearer eyJhbGciOiJub25lIn0.", StringComparison.Ordinal) && authorization.EndsWith(".app", StringComparison.Ordinal);

    public List<GraphCall> CallsTo(string method, string pathStart) =>
        Calls.Where(c => c.Method == method && c.Path.StartsWith(pathStart, StringComparison.Ordinal)).ToList();

    public static async Task<FakeGraphServer> StartAsync()
    {
        var port = FreePort();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();
        var server = new FakeGraphServer(app, port);
        app.Run(server.HandleAsync);
        await app.StartAsync();
        return server;
    }

    private async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        string body;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync();
        }

        var path = request.Path.Value ?? "/";
        Calls.Enqueue(new GraphCall(request.Method, path, Uri.UnescapeDataString(request.QueryString.Value ?? string.Empty), body,
            request.Headers.Authorization.ToString()));

        foreach (var (prefix, delay) in Slow)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                try
                {
                    await Task.Delay(delay, context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        foreach (var (prefix, raw) in Raw)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                context.Response.StatusCode = raw.Status;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(raw.Text);
                return;
            }
        }

        foreach (var (prefix, remaining) in Throttle)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal) && remaining > 0)
            {
                Throttle[prefix] = remaining - 1;
                context.Response.StatusCode = 429;
                context.Response.Headers.RetryAfter = "1";
                await context.Response.WriteAsJsonAsync(new { error = new { code = "TooManyRequests", message = "Rate limit" } });
                return;
            }
        }

        if (Forbidden.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = new { code = "Forbidden", message = "Missing role permissions on the request." } });
            return;
        }

        var (status, json) = path.Contains("/oauth2/v2.0/", StringComparison.Ordinal) ? Login(path, body)
            : path.StartsWith("/webhook/", StringComparison.Ordinal) ? (202, (JsonNode?)null)
            : Graph(request.Method, path["/v1.0".Length..], Uri.UnescapeDataString(request.QueryString.Value ?? string.Empty), body,
                request.Headers.Authorization.ToString());
        context.Response.StatusCode = status;
        if (json is not null)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(json.ToJsonString());
        }
    }

    private (int, JsonNode?) Login(string path, string body)
    {
        var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p.Length > 1 ? p[1].Replace('+', ' ') : string.Empty));
        if (path.EndsWith("/devicecode", StringComparison.Ordinal))
        {
            return (200, new JsonObject
            {
                ["device_code"] = "geraetecode-123", ["user_code"] = "ABCD-EFGH", ["verification_uri"] = "https://microsoft.com/devicelogin",
                ["expires_in"] = 900, ["interval"] = 5, ["message"] = "Code ABCD-EFGH eingeben",
            });
        }

        var grant = form.GetValueOrDefault("grant_type");
        if (grant == "client_credentials")
        {
            var ok = form.GetValueOrDefault("client_secret") == Secret
                || (form.GetValueOrDefault("client_assertion") is { } assertion && assertion.Split('.').Length == 3);
            return ok
                ? (200, new JsonObject { ["access_token"] = AppToken(), ["expires_in"] = 3599, ["token_type"] = "Bearer" })
                : (401, new JsonObject { ["error"] = "invalid_client", ["error_description"] = "AADSTS7000215: Invalid client secret provided." });
        }

        if (grant == "urn:ietf:params:oauth:grant-type:device_code")
        {
            if (Interlocked.Increment(ref _devicePolls) == 1)
            {
                return (400, new JsonObject { ["error"] = "authorization_pending", ["error_description"] = "AADSTS70016: pending" });
            }

            var claims = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"preferred_username\":\"estrichone-bot@firma.de\",\"oid\":\"bot-id\"}"))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return (200, new JsonObject
            {
                ["access_token"] = "user-token", ["refresh_token"] = "refresh-1", ["id_token"] = $"e30.{claims}.sig", ["expires_in"] = 3599,
            });
        }

        if (grant == "refresh_token")
        {
            var next = Interlocked.Increment(ref _refreshCount) + 1;
            return (200, new JsonObject { ["access_token"] = "user-token", ["refresh_token"] = $"refresh-{next}", ["expires_in"] = 3599 });
        }

        return (400, new JsonObject { ["error"] = "unsupported_grant_type" });
    }

    private (int, JsonNode?) Graph(string method, string path, string query, string body, string authorization)
    {
        if (!IsAppToken(authorization) && authorization != "Bearer user-token")
        {
            return (401, new JsonObject { ["error"] = new JsonObject { ["code"] = "InvalidAuthenticationToken" } });
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        switch (method, segments)
        {
            case ("POST", ["$batch"]):
                {
                    // JSON-Batching wie Graph: Teilanfragen einzeln beantworten (Rechte und Drosselung je Teilanfrage).
                    var responses = new JsonArray();
                    foreach (var request in JsonNode.Parse(body)?["requests"] as JsonArray ?? [])
                    {
                        var id = request?["id"]?.GetValue<string>();
                        var url = request?["url"]?.GetValue<string>() ?? "/";
                        var parts = url.Split('?', 2);
                        var subPath = "/v1.0" + parts[0];
                        var subQuery = parts.Length > 1 ? "?" + Uri.UnescapeDataString(parts[1]) : string.Empty;
                        BatchCalls.Enqueue(new GraphCall(request?["method"]?.GetValue<string>() ?? "GET", subPath, subQuery, string.Empty, authorization));
                        var throttled = Throttle.FirstOrDefault(t => subPath.StartsWith(t.Key, StringComparison.Ordinal) && t.Value > 0);
                        (int, JsonNode?) answer;
                        if (throttled.Key is not null)
                        {
                            Throttle[throttled.Key] = throttled.Value - 1;
                            answer = (429, Error("TooManyRequests", "Rate limit"));
                        }
                        else if (Forbidden.Any(prefix => subPath.StartsWith(prefix, StringComparison.Ordinal)))
                        {
                            answer = (403, Error("Forbidden", "Missing role permissions on the request."));
                        }
                        else
                        {
                            answer = Graph(request?["method"]?.GetValue<string>() ?? "GET", parts[0], subQuery, string.Empty, authorization);
                        }

                        var entry = new JsonObject { ["id"] = id, ["status"] = answer.Item1, ["body"] = answer.Item2 };
                        if (answer.Item1 == 429)
                        {
                            entry["headers"] = new JsonObject { ["Retry-After"] = "1" };
                        }

                        responses.Add(entry);
                    }

                    return (200, new JsonObject { ["responses"] = responses });
                }

            case ("GET", ["users"]):
                {
                    var match = System.Text.RegularExpressions.Regex.Match(query, "mail eq '([^']+)'");
                    var values = new JsonArray();
                    if (match.Success && Users.TryGetValue(match.Groups[1].Value.Replace("''", "'"), out var user))
                    {
                        values.Add(UserJson(match.Groups[1].Value, user));
                    }
                    else if (!match.Success && !Users.IsEmpty)
                    {
                        var first = Users.First();
                        values.Add(UserJson(first.Key, first.Value));
                    }

                    return (200, new JsonObject { ["value"] = values });
                }

            case ("GET", ["users", var id]):
                {
                    var user = Users.FirstOrDefault(u => u.Value.Id == id || u.Key.Equals(id, StringComparison.OrdinalIgnoreCase));
                    return user.Key is null ? (404, Error("Request_ResourceNotFound")) : (200, UserJson(user.Key, user.Value));
                }

            case ("GET", ["teams", var team]):
                return team == TeamId
                    ? (200, new JsonObject
                    {
                        ["id"] = TeamId, ["displayName"] = "Baustellen",
                        ["webUrl"] = $"https://teams.microsoft.com/l/team/19%3aabc%40thread.tacv2/conversations?groupId={TeamId}&tenantId=tenant",
                    })
                    : (404, Error("NotFound"));

            case ("POST", ["teams", var team, "channels"]):
                {
                    var name = JsonNode.Parse(body)?["displayName"]?.GetValue<string>() ?? "Kanal";
                    if (name.Contains("Doppelt", StringComparison.Ordinal))
                    {
                        return (400, Error("BadRequest", "Channel name already existed, please use other name."));
                    }

                    var id = $"19:{Guid.NewGuid():N}@thread.tacv2";
                    Channels[id] = false;
                    return (201, new JsonObject
                    {
                        ["id"] = id, ["displayName"] = name,
                        ["webUrl"] = $"https://teams.microsoft.com/l/channel/{Uri.EscapeDataString(id)}/{Uri.EscapeDataString(name)}?groupId={team}&tenantId=tenant",
                    });
                }

            case ("GET", ["teams", var team, "members"]):
                return (200, MembersJson(team));

            case ("POST", ["teams", var team, "members"]):
                return (201, AddMember(team, body));

            case ("POST", ["teams", _, "channels", var channel, "archive"]):
                return Channels.ContainsKey(channel) ? (202, Archive(channel, true)) : (404, Error("NotFound", "Channel not found"));

            case ("POST", ["teams", _, "channels", var channel, "unarchive"]):
                return Channels.ContainsKey(channel) ? (202, Archive(channel, false)) : (404, Error("NotFound", "Channel not found"));

            case ("DELETE", ["teams", _, "channels", var channel]):
                if (!Channels.TryRemove(channel, out _))
                {
                    return (404, Error("NotFound", "Channel not found"));
                }

                Deleted.Add(channel);
                return (204, null);

            case ("DELETE", ["chats", var chat]):
                if (!Chats.TryRemove(chat, out _))
                {
                    return (404, Error("NotFound", "Chat not found"));
                }

                Deleted.Add(chat);
                return (204, null);

            case ("POST", ["chats"]) when ChatType(body) == "oneOnOne":
                {
                    var ids = (JsonNode.Parse(body)?["members"] as JsonArray ?? []).Select(UserIdOf).Order(StringComparer.Ordinal).ToList();
                    var id = $"19:{string.Join("_", ids)}@unq.gbl.spaces";
                    Chats[id] = "oneOnOne";
                    return (201, new JsonObject
                    {
                        ["id"] = id, ["chatType"] = "oneOnOne", ["webUrl"] = $"https://teams.microsoft.com/l/chat/{Uri.EscapeDataString(id)}/0?tenantId=tenant",
                    });
                }

            case ("POST", ["chats"]):
                {
                    if ((JsonNode.Parse(body)?["members"] as JsonArray ?? []).Count < 3)
                    {
                        // Annahme nach Microsoft-Dokumentation/Teams-Verhalten: Gruppenchats erst ab drei Personen.
                        return (400, Error("BadRequest", "Group chat must have at least 3 members."));
                    }

                    var id = $"19:{Guid.NewGuid():N}@thread.v2";
                    Chats[id] = "group";
                    var members = Members.GetOrAdd(id, _ => new());
                    foreach (var member in JsonNode.Parse(body)?["members"] as JsonArray ?? [])
                    {
                        var userId = UserIdOf(member);
                        members[userId] = $"m-{userId}";
                    }

                    return (201, new JsonObject
                    {
                        ["id"] = id, ["chatType"] = "group", ["webUrl"] = $"https://teams.microsoft.com/l/chat/{Uri.EscapeDataString(id)}/0?tenantId=tenant",
                    });
                }

            case ("GET", ["chats", var chat, "members"]):
                return (200, MembersJson(chat));

            case ("POST", ["chats", var chat, "members"]):
                return (201, AddMember(chat, body));

            case ("DELETE", ["chats", var chat, "members", var membership]):
                {
                    var members = Members.GetOrAdd(chat, _ => new());
                    var entry = members.FirstOrDefault(m => m.Value == membership);
                    return entry.Key is null ? (404, Error("NotFound")) : (members.TryRemove(entry.Key, out _) ? (204, null) : (404, Error("NotFound")));
                }

            case ("POST", ["teams", _, "channels", _, "messages"]):
            case ("POST", ["chats", _, "messages"]):
                return authorization == "Bearer user-token"
                    ? (201, new JsonObject { ["id"] = Guid.NewGuid().ToString() })
                    : (403, Error("Forbidden", "Application permissions are not supported for sending messages."));

            default:
                return (404, Error("NotFound", $"{method} {path}"));
        }
    }

    private static string? ChatType(string body) => (JsonNode.Parse(body) as JsonObject)?["chatType"]?.GetValue<string>();

    private JsonObject? Archive(string channel, bool archived)
    {
        Channels[channel] = archived;
        return null;
    }

    private static JsonObject UserJson(string mail, (string Id, string Name) user) =>
        new() { ["id"] = user.Id, ["displayName"] = user.Name, ["userPrincipalName"] = mail.ToLowerInvariant(), ["mail"] = mail };

    private JsonObject MembersJson(string key)
    {
        var values = new JsonArray();
        foreach (var (userId, membership) in Members.GetOrAdd(key, _ => new()))
        {
            values.Add(new JsonObject { ["id"] = membership, ["userId"] = userId, ["@odata.type"] = "#microsoft.graph.aadUserConversationMember" });
        }

        return new JsonObject { ["value"] = values };
    }

    private JsonObject AddMember(string key, string body)
    {
        var userId = UserIdOf(JsonNode.Parse(body));
        Members.GetOrAdd(key, _ => new())[userId] = $"m-{userId}";
        return new JsonObject { ["id"] = $"m-{userId}", ["userId"] = userId };
    }

    private static string UserIdOf(JsonNode? member)
    {
        var bind = member?["user@odata.bind"]?.GetValue<string>() ?? string.Empty;
        var start = bind.IndexOf("('", StringComparison.Ordinal) + 2;
        return bind[start..bind.IndexOf("')", start, StringComparison.Ordinal)];
    }

    private static JsonObject Error(string code, string? message = null) =>
        new() { ["error"] = new JsonObject { ["code"] = code, ["message"] = message ?? code } };

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
