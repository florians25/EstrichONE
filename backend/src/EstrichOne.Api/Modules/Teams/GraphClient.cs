using System.Collections.Concurrent;
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
        var token = json["access_token"]?.GetValue<string>() ?? throw new GraphException("AUTH_FAILED", "Microsoft hat kein Zugriffstoken geliefert.");
        var seconds = json["expires_in"] is { } e ? ReadInt(e) : 3600;
        _tokens[key] = (token, clock.GetUtcNow().AddSeconds(Math.Max(60, seconds - 300)));
        return token;
    }

    // ---------- Anmeldung (Dienstkonto, Gerätecode) ----------

    public async Task<DeviceCodeStart> StartDeviceCodeAsync(string tenantId, string clientId, CancellationToken cancellationToken)
    {
        var url = $"{Options.LoginBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/devicecode";
        var json = await FormAsync(url, new Dictionary<string, string> { ["client_id"] = clientId, ["scope"] = SenderScopes }, cancellationToken);
        return new DeviceCodeStart(
            json["device_code"]?.GetValue<string>() ?? throw new GraphException("AUTH_FAILED", "Microsoft hat keinen Gerätecode geliefert."),
            json["user_code"]?.GetValue<string>() ?? string.Empty,
            json["verification_uri"]?.GetValue<string>() ?? "https://microsoft.com/devicelogin",
            json["expires_in"] is { } e ? ReadInt(e) : 900, json["interval"] is { } i ? ReadInt(i) : 5, json["message"]?.GetValue<string>());
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
        using var response = await SendWithRetryAsync(() =>
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            }

            return request;
        }, "graph.microsoft.com", cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }

        throw GraphError(response.StatusCode, text, permission);
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
        }, new Uri(url).Host, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new GraphException("WEBHOOK_FAILED", (int)response.StatusCode switch
            {
                401 or 403 => "Der Workflow hat die Nachricht abgelehnt (keine Berechtigung). Bitte den Workflow in Teams prüfen oder eine neue URL erzeugen.",
                404 => "Der Workflow wurde nicht gefunden (gelöscht oder URL unvollständig).",
                _ => $"Der Workflow meldet Fehler {(int)response.StatusCode}: {Clip(text, 200)}",
            }, (int)response.StatusCode);
        }
    }

    // ---------- intern ----------

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> create, string host, CancellationToken cancellationToken)
    {
        var client = http.CreateClient(HttpClientName);
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = create();
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (Exception exception) when (IsNetworkFailure(exception, cancellationToken))
            {
                if (attempt >= Options.MaxAttempts || IsPermanentNetworkFailure(exception))
                {
                    logger.LogWarning(exception, "Microsoft 365 nicht erreichbar ({Host})", host);
                    throw Offline(host);
                }

                await Task.Delay(Backoff(attempt), cancellationToken);
                continue;
            }

            var status = (int)response.StatusCode;
            if ((status is 429 or 503 or 504) && attempt < Options.MaxAttempts)
            {
                var delay = RetryAfter(response) ?? Backoff(attempt);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Microsoft 365 drosselt ({Status}); neuer Versuch {Attempt} in {Delay}", status, attempt + 1, delay);
                }

                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            return response;
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

    private static bool IsNetworkFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>Name nicht auflösbar oder Netz nicht erreichbar: Wiederholen hilft nicht (Server ohne Internet).</summary>
    private static bool IsPermanentNetworkFailure(Exception exception) =>
        exception.InnerException is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NetworkUnreachable or SocketError.NoData
            or SocketError.TryAgain or SocketError.HostUnreachable or SocketError.ConnectionRefused };

    public static GraphException Offline(string host) => new("OFFLINE",
        $"Microsoft 365 ist vom Server aus nicht erreichbar ({host}). Für Teams braucht der EstrichONE-Server eine Internetverbindung (HTTPS, Port 443 zu "
        + "login.microsoftonline.com und graph.microsoft.com) – bitte Firewall oder Proxy prüfen. Ohne Internet bleibt der eingebaute Chat nutzbar.");

    private async Task<JsonObject> TokenRequestAsync(string tenantId, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"{Options.LoginBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/token";
        return await FormAsync(url, form, cancellationToken);
    }

    private async Task<JsonObject> FormAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) },
            "login.microsoftonline.com", cancellationToken);
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

        var error = json?["error"]?.GetValue<string>();
        var description = json?["error_description"]?.GetValue<string>() ?? Clip(text, 300);
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

    private static GraphException GraphError(HttpStatusCode status, string text, string permission)
    {
        string? code = null;
        string? message = null;
        try
        {
            var error = JsonNode.Parse(text)?["error"];
            code = error?["code"]?.GetValue<string>();
            message = error?["message"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            message = Clip(text, 200);
        }

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
        var access = json["access_token"]?.GetValue<string>() ?? throw new GraphException("AUTH_FAILED", "Microsoft hat kein Zugriffstoken geliefert.");
        string? name = null;
        string? id = null;
        if (json["id_token"]?.GetValue<string>() is { } idToken && idToken.Split('.') is { Length: >= 2 } parts)
        {
            try
            {
                var claims = JsonNode.Parse(Base64UrlDecode(parts[1]));
                name = claims?["preferred_username"]?.GetValue<string>() ?? claims?["name"]?.GetValue<string>();
                id = claims?["oid"]?.GetValue<string>();
            }
            catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
            {
                // Name ist nur Anzeige.
            }
        }

        return new DelegatedToken(access, json["refresh_token"]?.GetValue<string>(), name, id);
    }

    private static string CacheKey(GraphCredentials c) =>
        $"{c.TenantId}|{c.ClientId}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.Secret ?? c.CertificatePem ?? string.Empty)))}";

    private static int ReadInt(JsonNode node) => node.GetValueKind() == JsonValueKind.String
        ? int.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture)
        : node.GetValue<int>();

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
