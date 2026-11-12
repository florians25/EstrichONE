using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Teams;

/// <summary>Adressen von Microsoft 365 und Verhalten bei Drosselung (<c>EstrichOne:Teams</c>; Tests zeigen auf einen nachgebildeten Server).</summary>
public sealed class TeamsOptions
{
    public const string SectionName = "EstrichOne:Teams";

    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com";
    public string LoginBaseUrl { get; set; } = "https://login.microsoftonline.com";

    /// <summary>Versuche je Aufruf bei 429/503/504 oder Netzwerkfehlern.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Längste Wartezeit zwischen zwei Versuchen (auch wenn Retry-After mehr verlangt).</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Erlaubte Hosts (Endungen) für Workflows-Webhooks.</summary>
    public string[] WebhookHosts { get; set; } = ["logic.azure.com", "powerplatform.com", "powerautomate.com", "webhook.office.com"];

    /// <summary>Höchstdauer eines einzelnen Versuchs im Hintergrund (Abgleich, Karten).</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gesamtfrist aller Microsoft-Aufrufe einer Anfrage aus der Oberfläche. Deutlich unter den Zeitgrenzen vorgeschalteter
    /// Proxys (Cloudflare 100 s, Nginx 60 s), damit die Oberfläche immer eine verständliche Antwort bekommt.
    /// </summary>
    public TimeSpan InteractiveDeadline { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>Höchstdauer eines einzelnen Versuchs bei Anfragen aus der Oberfläche.</summary>
    public TimeSpan InteractiveAttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Versuche je Aufruf bei Anfragen aus der Oberfläche (Wartezeiten nur, solange die Gesamtfrist reicht).</summary>
    public int InteractiveMaxAttempts { get; set; } = 2;

    /// <summary>Anfragen je Graph-<c>$batch</c> (Microsoft erlaubt höchstens 20).</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Nur für Tests: Webhooks auch über http (Loopback).</summary>
    public bool AllowHttpWebhooks { get; set; }

    /// <summary>Abstand des Hintergrundabgleichs (Mitglieder, automatische Verknüpfung).</summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(10);
}

public sealed record GraphCredentials(string TenantId, string ClientId, string? Secret, string? CertificatePem);

public sealed record DeviceCodeStart(string DeviceCode, string UserCode, string VerificationUri, int ExpiresInSeconds, int IntervalSeconds, string? Message);

public sealed record DelegatedToken(string AccessToken, string? RefreshToken, string? UserName, string? UserId);

/// <summary>Fehler bei Microsoft 365 mit verständlicher Ursache; <see cref="Code"/> geht als Problem-Code an die Oberfläche.</summary>
public sealed class GraphException(string code, string message, int? status = null) : Exception(message)
{
    public string Code { get; } = code;
    public int? Status { get; } = status;
}

/// <summary>
/// Gekapselter Zugang zu Microsoft Graph und zur Anmeldung bei Entra ID: Token je App-Registrierung zwischengespeichert,
/// Wiederholung bei Drosselung (429 mit Retry-After) und kurzzeitigen Störungen (503/504, Netzwerk), verständliche
/// Fehlermeldungen (fehlende Berechtigung, falscher Schlüssel, keine Internetverbindung). Kein Microsoft-SDK: wenige
/// Aufrufe, volle Kontrolle über Wiederholung und Zeitüberschreitung, testbar gegen einen nachgebildeten Server.
/// </summary>
public sealed class GraphClient(IHttpClientFactory http, IOptions<TeamsOptions> options, TimeProvider clock, ILogger<GraphClient> logger)
{
    public const string HttpClientName = "teams-graph";
    public const string GraphScope = "https://graph.microsoft.com/.default";

    /// <summary>Delegierte Rechte des Dienstkontos (nur Nachrichten senden).</summary>
    public const string SenderScopes = "openid profile offline_access https://graph.microsoft.com/ChannelMessage.Send https://graph.microsoft.com/ChatMessage.Send";

    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _tokens = new();

    private TeamsOptions Options => options.Value;

    public void Forget() => _tokens.Clear();

    // ---------- Anmeldung (Anwendung) ----------

    public async Task<string> AppTokenAsync(GraphCredentials credentials, CancellationToken cancellationToken)
    {
        var key = CacheKey(credentials);
        if (_tokens.TryGetValue(key, out var cached) && cached.Expires > clock.GetUtcNow())
        {
            return cached.Token;
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = credentials.ClientId, ["scope"] = GraphScope, ["grant_type"] = "client_credentials",
        };
        AddClientAuthentication(form, credentials);
        var json = await TokenRequestAsync(credentials.TenantId, form, cancellationToken);
        var token = Str(json["access_token"]) ?? throw new GraphException("AUTH_FAILED", "Microsoft hat kein Zugriffstoken geliefert.");
        var seconds = json["expires_in"] is { } e ? ReadInt(e, 3600) : 3600;
        _tokens[key] = (token, clock.GetUtcNow().AddSeconds(Math.Max(60, seconds - 300)));
        return token;
    }

    // ---------- Anmeldung (Dienstkonto, Gerätecode) ----------

    public async Task<DeviceCodeStart> StartDeviceCodeAsync(string tenantId, string clientId, CancellationToken cancellationToken)
    {
        var url = $"{Options.LoginBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/devicecode";
        var json = await FormAsync(url, new Dictionary<string, string> { ["client_id"] = clientId, ["scope"] = SenderScopes }, "Gerätecode anfordern",
            cancellationToken);
        return new DeviceCodeStart(
            Str(json["device_code"]) ?? throw new GraphException("AUTH_FAILED", "Microsoft hat keinen Gerätecode geliefert."),
            Str(json["user_code"]) ?? string.Empty,
            Str(json["verification_uri"]) ?? "https://microsoft.com/devicelogin",
            json["expires_in"] is { } e ? ReadInt(e, 900) : 900, json["interval"] is { } i ? ReadInt(i, 5) : 5, Str(json["message"]));
    }

    /// <summary><c>null</c>, solange die Anmeldung aussteht.</summary>
    public async Task<DelegatedToken?> PollDeviceCodeAsync(string tenantId, string clientId, string deviceCode, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["device_code"] = deviceCode,
        };
        try
        {
            return Delegated(await TokenRequestAsync(tenantId, form, cancellationToken));
        }
        catch (GraphException exception) when (exception.Code is "AUTHORIZATION_PENDING" or "SLOW_DOWN")
        {
            return null;
        }
    }

    public async Task<DelegatedToken> RefreshAsync(GraphCredentials credentials, string refreshToken, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = credentials.ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["scope"] = SenderScopes,
        };
        AddClientAuthentication(form, credentials);
        return Delegated(await TokenRequestAsync(credentials.TenantId, form, cancellationToken));
    }

    // ---------- Graph ----------

    public Task<JsonNode?> GetAsync(string token, string path, string permission, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, token, path, null, permission, cancellationToken);

    public Task<JsonNode?> PostAsync(string token, string path, JsonNode body, string permission, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, token, path, body, permission, cancellationToken);

    public Task<JsonNode?> DeleteAsync(string token, string path, string permission, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, token, path, null, permission, cancellationToken);

    public async Task<JsonNode?> SendAsync(HttpMethod method, string token, string path, JsonNode? body, string permission, CancellationToken cancellationToken)
    {
        var url = $"{Options.GraphBaseUrl.TrimEnd('/')}/v1.0/{path.TrimStart('/')}";
        var step = $"{method.Method} {StepName(path)}";
        using var response = await SendWithRetryAsync(() =>
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }

            return request;
        }, "graph.microsoft.com", step, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return string.IsNullOrWhiteSpace(text) ? null : ParseJson(text, step, (int)response.StatusCode);
        }

        throw GraphError(step, response.StatusCode, text, permission);
    }

    /// <summary>Ergebnis einer Teilanfrage aus <see cref="BatchGetAsync"/>: Inhalt oder Fehler.</summary>
    public sealed record BatchItem(JsonNode? Body, GraphException? Error);

    /// <summary>
    /// Viele lesende Aufrufe gebündelt (Graph-<c>$batch</c>, je Bündel höchstens 20): N Benutzer zuordnen kostet N/20 Aufrufe
    /// statt N. Gedrosselte Teilanfragen (429) werden nach Retry-After wiederholt, solange Versuche und Frist reichen.
    /// </summary>
    public async Task<IReadOnlyList<BatchItem>> BatchGetAsync(string token, IReadOnlyList<string> paths, string permission,
        CancellationToken cancellationToken)
    {
        var results = new BatchItem?[paths.Count];
        var size = Math.Clamp(Options.BatchSize, 1, 20);
        var maxAttempts = MaxAttempts(Current.Value);
        for (var start = 0; start < paths.Count; start += size)
        {
            var pending = Enumerable.Range(start, Math.Min(size, paths.Count - start)).ToList();
            for (var round = 1; pending.Count > 0; round++)
            {
                var requests = new JsonArray();
                foreach (var i in pending)
                {
                    requests.Add(new JsonObject
                    {
                        ["id"] = i.ToString(CultureInfo.InvariantCulture), ["method"] = "GET", ["url"] = "/" + paths[i].TrimStart('/'),
                    });
                }

                var answer = await PostAsync(token, "$batch", new JsonObject { ["requests"] = requests }, permission, cancellationToken);
                var throttled = new List<int>();
                var wait = TimeSpan.Zero;
                foreach (var response in (answer?["responses"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (!int.TryParse(Str(response["id"]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) || !pending.Contains(i)
                        || results[i] is not null)
                    {
                        continue;
                    }

                    var status = response["status"] is { } s ? ReadInt(s, 0) : 0;
                    var body = response["body"]?.DeepClone();
                    if (status is >= 200 and < 300)
                    {
                        results[i] = new BatchItem(body, null);
                    }
                    else if (status is 429 or 503 or 504 && round < maxAttempts)
                    {
                        throttled.Add(i);
                        var retryAfter = Str(response["headers"]?["Retry-After"]) is { } header
                            && int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                                ? TimeSpan.FromSeconds(seconds)
                                : Backoff(round);
                        wait = TimeSpan.FromTicks(Math.Clamp(Math.Max(wait.Ticks, retryAfter.Ticks), 0, Options.MaxRetryDelay.Ticks));
                    }
                    else
                    {
                        results[i] = new BatchItem(null, GraphError($"GET {StepName(paths[i])} (Batch)", (HttpStatusCode)status,
                            body?.ToJsonString() ?? string.Empty, permission));
                    }
                }

                foreach (var i in pending.Where(i => results[i] is null && !throttled.Contains(i)))
                {
                    results[i] = new BatchItem(null, new GraphException("UNEXPECTED_RESPONSE", "Microsoft 365 hat auf eine Teilanfrage nicht geantwortet."));
                }

                if (throttled.Count > 0)
                {
                    if (Current.Value is { } interactive && wait >= interactive.Remaining)
                    {
                        logger.LogWarning("Microsoft Graph $batch: {Count} Teilanfragen gedrosselt, Frist reicht nicht für einen weiteren Versuch", throttled.Count);
                        foreach (var i in throttled)
                        {
                            results[i] = new BatchItem(null, Throttled());
                        }

                        break;
                    }

                    await DelayAsync(wait, "graph.microsoft.com", "POST $batch", cancellationToken);
                }

                pending = throttled;
            }
        }

        return results.Select(r => r ?? new BatchItem(null, Throttled())).ToList();
    }

    // ---------- Webhook (Teams-Workflows) ----------

    /// <summary>Prüft eine Workflows-URL (https, bekannter Microsoft-Host). Liefert die Fehlermeldung oder <c>null</c>.</summary>
    public string? ValidateWebhook(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return "Bitte die vollständige Webhook-URL aus dem Teams-Workflow einfügen.";
        }

        var insecureAllowed = Options.AllowHttpWebhooks && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !insecureAllowed)
        {
            return "Die Webhook-URL muss mit https:// beginnen.";
        }

        var host = uri.Host.ToLowerInvariant();
        return insecureAllowed || Options.WebhookHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal))
            ? null
            : $"Unbekannter Host „{uri.Host}“. Erwartet wird eine URL aus Teams › Workflows (z. B. …logic.azure.com oder …powerplatform.com).";
    }

    public async Task PostWebhookAsync(string url, JsonNode card, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["type"] = "message",
            ["attachments"] = new JsonArray(new JsonObject
            {
                ["contentType"] = "application/vnd.microsoft.card.adaptive", ["contentUrl"] = null, ["content"] = card.DeepClone(),
            }),
        };
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        }, Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "Webhook", "POST Webhook", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Teams-Workflow-Webhook ({Host}) lehnt ab: HTTP {Status}", uri?.Host, (int)response.StatusCode);
            throw new GraphException("WEBHOOK_FAILED", (int)response.StatusCode switch
            {
                401 or 403 => "Der Workflow hat die Nachricht abgelehnt (keine Berechtigung). Bitte den Workflow in Teams prüfen oder eine neue URL erzeugen.",
                404 => "Der Workflow wurde nicht gefunden (gelöscht oder URL unvollständig).",
                _ => $"Der Workflow meldet Fehler {(int)response.StatusCode}: {Clip(text, 200)}",
            }, (int)response.StatusCode);
        }
    }

    // ---------- intern ----------

    // ---------- Frist für Anfragen aus der Oberfläche ----------

    private static readonly AsyncLocal<InteractiveScope?> Current = new();

    /// <summary>
    /// Ab hier gelten für alle Microsoft-Aufrufe (dieses asynchronen Ablaufs) die kurze Gesamtfrist, kürzere Versuche und weniger
    /// Wiederholungen der Oberfläche. Läuft die Frist ab, endet jeder Aufruf mit <c>TIMEOUT</c> statt die Verbindung offen zu halten,
    /// bis ein vorgeschalteter Proxy sie kappt. Die Datenbank ist davon nicht betroffen (Ergebnisse werden gespeichert).
    /// </summary>
    public IDisposable BeginInteractive(TimeSpan? deadline = null)
    {
        var scope = new InteractiveScope(deadline ?? Options.InteractiveDeadline, Current.Value);
        Current.Value = scope;
        return scope;
    }

    private sealed class InteractiveScope : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly TimeSpan _deadline;
        private readonly InteractiveScope? _previous;

        public InteractiveScope(TimeSpan deadline, InteractiveScope? previous)
        {
            _deadline = deadline;
            _previous = previous;
            _cts = new CancellationTokenSource(deadline);
        }

        public bool Ended { get; private set; }

        public CancellationToken Token => Ended ? CancellationToken.None : _cts.Token;

        public TimeSpan Remaining => _deadline - Stopwatch.GetElapsedTime(_started);

        public bool Expired => !Ended && (_cts.IsCancellationRequested || Remaining <= TimeSpan.Zero);

        public void Dispose()
        {
            if (Ended)
            {
                return;
            }

            Ended = true;
            Current.Value = _previous;
            _cts.Dispose();
        }
    }

    private int MaxAttempts(InteractiveScope? interactive) =>
        Math.Max(1, interactive is null ? Options.MaxAttempts : Math.Min(Options.MaxAttempts, Options.InteractiveMaxAttempts));

    // ---------- intern ----------

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> create, string host, string step,
        CancellationToken cancellationToken)
    {
        var interactive = Current.Value is { Ended: false } scope ? scope : null;
        var maxAttempts = MaxAttempts(interactive);
        var client = http.CreateClient(HttpClientName);
        for (var attempt = 1; ; attempt++)
        {
            var attemptTimeout = interactive is null ? Options.AttemptTimeout : Options.InteractiveAttemptTimeout;
            if (interactive is not null)
            {
                if (interactive.Expired)
                {
                    logger.LogWarning("Microsoft 365 {Step}: Frist von {Deadline} s abgelaufen, Aufruf nicht mehr gestartet", step,
                        Options.InteractiveDeadline.TotalSeconds);
                    throw TimedOut(host);
                }

                attemptTimeout = interactive.Remaining < attemptTimeout ? interactive.Remaining : attemptTimeout;
            }

            HttpResponseMessage response;
            using (var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, interactive?.Token ?? CancellationToken.None))
            {
                attemptCts.CancelAfter(attemptTimeout);
                try
                {
                    using var request = create();
                    response = await client.SendAsync(request, attemptCts.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
                {
                    var timedOut = exception is OperationCanceledException;
                    if (attempt >= maxAttempts || interactive?.Expired == true || (!timedOut && IsPermanentNetworkFailure(exception)))
                    {
                        if (timedOut)
                        {
                            logger.LogWarning("Microsoft 365 {Step}: keine Antwort von {Host} (Zeitüberschreitung nach {Attempt} Versuch(en))", step, host, attempt);
                            throw TimedOut(host);
                        }

                        logger.LogWarning(exception, "Microsoft 365 {Step}: {Host} nicht erreichbar ({Attempt} Versuch(e))", step, host, attempt);
                        throw Offline(host);
                    }

                    await DelayAsync(Backoff(attempt), host, step, cancellationToken);
                    continue;
                }
            }

            var status = (int)response.StatusCode;
            if ((status is 429 or 503 or 504) && attempt < maxAttempts)
            {
                var delay = RetryAfter(response) ?? Backoff(attempt);
                if (interactive is null || delay < interactive.Remaining)
                {
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("Microsoft 365 {Step}: HTTP {Status} (Drosselung/Störung); neuer Versuch {Attempt} in {Delay}", step, status,
                            attempt + 1, delay);
                    }

                    response.Dispose();
                    await DelayAsync(delay, host, step, cancellationToken);
                    continue;
                }
            }

            return response;
        }
    }

    /// <summary>Warten vor dem nächsten Versuch; endet die Frist der Oberfläche vorher: <c>TIMEOUT</c>.</summary>
    private async Task DelayAsync(TimeSpan delay, string host, string step, CancellationToken cancellationToken)
    {
        var interactive = Current.Value is { Ended: false } scope ? scope : null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, interactive?.Token ?? CancellationToken.None);
        try
        {
            await Task.Delay(delay, linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Microsoft 365 {Step}: Frist abgelaufen, während auf einen neuen Versuch gewartet wurde ({Host})", step, host);
            throw TimedOut(host);
        }
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        TimeSpan? delay = header?.Delta ?? (header?.Date is { } date ? date - clock.GetUtcNow() : null);
        if (delay is null && response.Headers.TryGetValues("Retry-After", out var values)
            && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            delay = TimeSpan.FromSeconds(seconds);
        }

        return delay is null ? null : TimeSpan.FromTicks(Math.Clamp(delay.Value.Ticks, 0, Options.MaxRetryDelay.Ticks));
    }

    private TimeSpan Backoff(int attempt)
    {
        var seconds = Math.Pow(2, attempt - 1) + Random.Shared.NextDouble() * 0.5;
        return TimeSpan.FromSeconds(Math.Min(seconds, Options.MaxRetryDelay.TotalSeconds));
    }

    /// <summary>Name nicht auflösbar oder Netz nicht erreichbar: Wiederholen hilft nicht (Server ohne Internet).</summary>
    private static bool IsPermanentNetworkFailure(Exception exception) =>
        exception.InnerException is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NetworkUnreachable or SocketError.NoData
            or SocketError.TryAgain or SocketError.HostUnreachable or SocketError.ConnectionRefused };

    public static GraphException Offline(string host) => new("OFFLINE",
        $"Microsoft 365 ist vom Server aus nicht erreichbar ({host}). Für Teams braucht der EstrichONE-Server eine Internetverbindung (HTTPS, Port 443 zu "
        + "login.microsoftonline.com und graph.microsoft.com) – bitte Firewall oder Proxy prüfen. Ohne Internet bleibt der eingebaute Chat nutzbar.");

    public static GraphException TimedOut(string host) => new("TIMEOUT",
        $"Microsoft 365 antwortet nicht rechtzeitig ({host}). Bitte in einigen Minuten erneut versuchen. Bleibt es dabei, die Internetverbindung "
        + "des Servers (Firewall, Proxy) prüfen; Einzelheiten stehen im Server-Log (docker compose logs api).");

    private static GraphException Throttled() =>
        new("THROTTLED", "Microsoft drosselt die Anfragen gerade. Bitte in einigen Minuten erneut versuchen.", 429);

    private JsonNode? ParseJson(string text, string step, int status)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            logger.LogWarning("Microsoft 365 {Step}: HTTP {Status}, Antwort ist kein JSON ({Length} Zeichen)", step, status, text.Length);
            throw new GraphException("UNEXPECTED_RESPONSE",
                "Microsoft 365 hat eine unerwartete Antwort geliefert (kein JSON). Möglicherweise schaltet sich ein Proxy oder eine Firewall dazwischen; "
                + "Einzelheiten stehen im Server-Log.", status);
        }
    }

    /// <summary>Pfad ohne Abfrage (keine E-Mail-Adressen aus Filtern im Log).</summary>
    private static string StepName(string path) => Clip(path.Split('?', 2)[0].TrimStart('/'), 120);

    private async Task<JsonObject> TokenRequestAsync(string tenantId, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"{Options.LoginBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/token";
        return await FormAsync(url, form, $"Anmeldung ({form.GetValueOrDefault("grant_type")})", cancellationToken);
    }

    private async Task<JsonObject> FormAsync(string url, Dictionary<string, string> form, string step, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) },
            "login.microsoftonline.com", step, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonObject? json = null;
        try
        {
            json = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            // keine JSON-Antwort (z. B. Proxy-Fehlerseite)
        }

        if (response.IsSuccessStatusCode && json is not null)
        {
            return json;
        }

        var error = Str(json?["error"]);
        var description = Str(json?["error_description"]) ?? Clip(text, 300);
        if (json is null)
        {
            logger.LogWarning("Microsoft Entra ID {Step}: HTTP {Status}, Antwort ist kein JSON ({Length} Zeichen)", step, (int)response.StatusCode, text.Length);
            throw new GraphException("UNEXPECTED_RESPONSE",
                $"Die Anmeldung bei Microsoft lieferte keine gültige Antwort (HTTP {(int)response.StatusCode}). Möglicherweise schaltet sich ein Proxy oder "
                + "eine Firewall dazwischen; Einzelheiten stehen im Server-Log.", (int)response.StatusCode);
        }

        if (error is not ("authorization_pending" or "slow_down"))
        {
            logger.LogWarning("Microsoft Entra ID {Step}: HTTP {Status}, Fehler {Error}, Code {Code}", step, (int)response.StatusCode, error,
                description.Split(':', 2)[0].Trim());
        }

        throw error switch
        {
            "authorization_pending" => new GraphException("AUTHORIZATION_PENDING", "Anmeldung steht noch aus."),
            "slow_down" => new GraphException("SLOW_DOWN", "Anmeldung steht noch aus."),
            "expired_token" or "code_expired" => new GraphException("DEVICE_CODE_EXPIRED", "Der Anmeldecode ist abgelaufen. Bitte neu starten."),
            "authorization_declined" => new GraphException("DEVICE_CODE_DECLINED", "Die Anmeldung wurde abgelehnt."),
            _ => new GraphException("AUTH_FAILED", DescribeLogin(description), (int)response.StatusCode),
        };
    }

    internal static string DescribeLogin(string description)
    {
        var code = description.Split(':', 2)[0].Trim();
        return code switch
        {
            "AADSTS7000215" => "Der geheime Clientschlüssel ist ungültig. Bitte den WERT des Schlüssels eintragen (nicht die Schlüssel-ID).",
            "AADSTS7000222" => "Der geheime Clientschlüssel ist abgelaufen. Bitte in Entra ID einen neuen Schlüssel erstellen und hier eintragen.",
            "AADSTS700016" => "Die Anwendung (Client-ID) wurde im Mandanten nicht gefunden. Bitte Client-ID und Tenant-ID prüfen.",
            "AADSTS90002" or "AADSTS900023" or "AADSTS90013" => "Der Mandant (Tenant-ID) wurde nicht gefunden. Bitte die Verzeichnis-ID (Mandanten-ID) prüfen.",
            "AADSTS700027" or "AADSTS700023" => "Das Zertifikat ist in der App-Registrierung nicht hinterlegt oder passt nicht. Bitte den öffentlichen Teil hochladen.",
            "AADSTS7000218" => "Für das Dienstkonto muss in der App-Registrierung unter „Authentifizierung“ die Option „Öffentliche Clientflows zulassen“ auf „Ja“ stehen.",
            "AADSTS65001" => "Die Administratorzustimmung fehlt. Bitte in Entra ID unter „API-Berechtigungen“ „Administratorzustimmung erteilen“ wählen.",
            "AADSTS50076" or "AADSTS50079" => "Für das Dienstkonto ist eine zusätzliche Bestätigung (MFA) nötig – bitte die Anmeldung im Browser abschließen.",
            _ => $"Anmeldung bei Microsoft fehlgeschlagen: {Clip(description.Split('\n')[0], 300)}",
        };
    }

    private GraphException GraphError(string step, HttpStatusCode status, string text, string permission)
    {
        string? code = null;
        string? message = null;
        try
        {
            var error = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text)?["error"];
            code = Str(error?["code"]);
            message = Str(error?["message"]);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            message = Clip(text, 200);
        }

        logger.LogWarning("Microsoft Graph {Step}: HTTP {Status}, Graph-Code {GraphCode}, benötigte Berechtigung {Permission}: {GraphMessage}", step,
            (int)status, code ?? "-", permission, Clip(message, 300));

        return (int)status switch
        {
            401 => new GraphException("AUTH_FAILED", "Microsoft hat die Anmeldung der App abgelehnt. Bitte Tenant-ID, Client-ID und Schlüssel prüfen.", 401),
            403 => new GraphException("FORBIDDEN",
                $"Microsoft verweigert den Zugriff – es fehlt die Berechtigung „{permission}“. Bitte in Entra ID unter „API-Berechtigungen“ hinzufügen "
                + "und „Administratorzustimmung erteilen“ wählen (siehe Handbuch).", 403),
            404 => new GraphException("NOT_FOUND", $"Bei Microsoft nicht gefunden ({message ?? code ?? "404"}).", 404),
            409 => new GraphException("CONFLICT", $"Bei Microsoft bereits vorhanden ({message ?? code ?? "409"}).", 409),
            429 => new GraphException("THROTTLED", "Microsoft drosselt die Anfragen gerade. Bitte in einigen Minuten erneut versuchen.", 429),
            400 => new GraphException("GRAPH_REJECTED", $"Microsoft lehnt die Anfrage ab: {Clip(message ?? code ?? text, 300)}", 400),
            _ => new GraphException("GRAPH_ERROR", $"Microsoft meldet Fehler {(int)status}: {Clip(message ?? code ?? text, 300)}", (int)status),
        };
    }

    private void AddClientAuthentication(Dictionary<string, string> form, GraphCredentials credentials)
    {
        if (credentials.CertificatePem is { } pem)
        {
            form["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
            form["client_assertion"] = ClientAssertion(credentials, pem);
        }
        else if (credentials.Secret is { } secret)
        {
            form["client_secret"] = secret;
        }
    }

    /// <summary>Signiertes JWT für die Anmeldung mit Zertifikat (RFC 7523, wie MSAL).</summary>
    private string ClientAssertion(GraphCredentials credentials, string pem)
    {
        using var certificate = LoadCertificate(pem);
        using var rsa = certificate.GetRSAPrivateKey() ?? throw new GraphException("CERTIFICATE_INVALID", "Das Zertifikat enthält keinen RSA-Schlüssel.");
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var header = new JsonObject
        {
            ["alg"] = "RS256", ["typ"] = "JWT", ["x5t"] = Base64Url(certificate.GetCertHash()),
            ["x5t#S256"] = Base64Url(certificate.GetCertHash(HashAlgorithmName.SHA256)),
        };
        var payload = new JsonObject
        {
            ["aud"] = $"{Options.LoginBaseUrl.TrimEnd('/')}/{credentials.TenantId}/oauth2/v2.0/token", ["iss"] = credentials.ClientId,
            ["sub"] = credentials.ClientId, ["jti"] = Guid.NewGuid().ToString(), ["nbf"] = now - 60, ["iat"] = now, ["exp"] = now + 600,
        };
        var unsigned = $"{Base64Url(Encoding.UTF8.GetBytes(header.ToJsonString()))}.{Base64Url(Encoding.UTF8.GetBytes(payload.ToJsonString()))}";
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{unsigned}.{Base64Url(signature)}";
    }

    /// <summary>PEM mit Zertifikat und privatem Schlüssel (in einem Text).</summary>
    public static X509Certificate2 LoadCertificate(string pem)
    {
        try
        {
            return X509Certificate2.CreateFromPem(pem, pem);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new GraphException("CERTIFICATE_INVALID",
                "Zertifikat nicht lesbar. Bitte Zertifikat und privaten Schlüssel im PEM-Format einfügen (-----BEGIN CERTIFICATE----- … -----BEGIN PRIVATE KEY----- …).");
        }
    }

    private static DelegatedToken Delegated(JsonObject json)
    {
        var access = Str(json["access_token"]) ?? throw new GraphException("AUTH_FAILED", "Microsoft hat kein Zugriffstoken geliefert.");
        string? name = null;
        string? id = null;
        if (Str(json["id_token"]) is { } idToken && idToken.Split('.') is { Length: >= 2 } parts)
        {
            try
            {
                var claims = JsonNode.Parse(Base64UrlDecode(parts[1]));
                name = Str(claims?["preferred_username"]) ?? Str(claims?["name"]);
                id = Str(claims?["oid"]);
            }
            catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
            {
                // Name ist nur Anzeige.
            }
        }

        return new DelegatedToken(access, Str(json["refresh_token"]), name, id);
    }

    private static string CacheKey(GraphCredentials c) =>
        $"{c.TenantId}|{c.ClientId}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.Secret ?? c.CertificatePem ?? string.Empty)))}";

    private static int ReadInt(JsonNode node, int fallback) => node switch
    {
        JsonValue value when value.TryGetValue<int>(out var number) => number,
        JsonValue value when value.TryGetValue<string>(out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            => parsed,
        _ => fallback,
    };

    /// <summary>Zeichenkette aus JSON; anderer Typ oder fehlend: <c>null</c> (nie eine Ausnahme bei unerwarteten Antworten).</summary>
    internal static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    internal static string Clip(string? text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..max] + "…";
}
