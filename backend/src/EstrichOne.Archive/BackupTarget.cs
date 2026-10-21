using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http.Features;

namespace EstrichOne.Archive;

/// <summary>
/// Backup-Ziel: startet den bewährten restic-REST-Server (nur lokal, „append-only“, private Repositories) und
/// reicht Anfragen nach Prüfung der Zugangsdaten über HTTPS an ihn durch. Der REST-Server erfährt den Benutzer über
/// eine interne Kopfzeile, die von außen nie durchgelassen wird.
/// </summary>
public sealed class RestServerHost(ArchiveOptions options, ILogger<RestServerHost> logger) : BackgroundService
{
    public const string UserHeader = "X-EstrichOne-Rest-User";

    public bool Running { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(options.PathOf("repos"));
        while (!stoppingToken.IsCancellationRequested)
        {
            var arguments = new List<string>
            {
                "--path", options.PathOf("repos"), "--listen", $"127.0.0.1:{options.RestServerPort}",
                "--private-repos", "--proxy-auth-username", UserHeader,
            };
            if (options.AppendOnly)
            {
                arguments.Add("--append-only");
            }

            using var process = new Process { StartInfo = new ProcessStartInfo(options.RestServerPath) { UseShellExecute = false } };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            try
            {
                process.Start();
                Running = true;
                logger.LogInformation("restic-REST-Server gestartet (append-only: {AppendOnly}).", options.AppendOnly);
                await process.WaitForExitAsync(stoppingToken);
                logger.LogWarning("restic-REST-Server beendet (Code {ExitCode}) – Neustart in 5 Sekunden.", process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }

                break;
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                logger.LogError(exception, "restic-REST-Server {Path} nicht startbar.", options.RestServerPath);
            }
            finally
            {
                Running = false;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

public static class ResticProxy
{
    private static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Connection", "Host", "Authorization", RestServerHost.UserHeader,
    };

    public static async Task HandleAsync(HttpContext context, string path, ClientRegistry clients, ArchiveOptions options,
        IHttpClientFactory httpClients)
    {
        if (!TryBasic(context.Request.Headers.Authorization.ToString(), out var user, out var password)
            || clients.AuthenticateRest(user, password) is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"EstrichONE Backup\"";
            return;
        }

        // Nur das eigene Repository: /<benutzer>/…
        if (!(path == user || path.StartsWith(user + "/", StringComparison.Ordinal)) || path.Contains("..", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize = null;
        var target = new Uri($"http://127.0.0.1:{options.RestServerPort}/{path}{context.Request.QueryString}");
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        if (context.Request.ContentLength > 0 || context.Request.Headers.TransferEncoding.Count > 0)
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentLength is { } length)
            {
                request.Content.Headers.ContentLength = length;
            }

            if (context.Request.ContentType is { } type)
            {
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
            }
        }

        foreach (var header in context.Request.Headers)
        {
            if (!HopHeaders.Contains(header.Key) && !header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        request.Headers.TryAddWithoutValidation(RestServerHost.UserHeader, user);
        using var http = httpClients.CreateClient("rest-server");
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                if (!HopHeaders.Contains(header.Key))
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
    }

    internal static bool TryBasic(string header, out string user, out string password)
    {
        user = password = string.Empty;
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
            var colon = decoded.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return false;
            }

            user = decoded[..colon];
            password = decoded[(colon + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Belegter Platz eines Repositorys und Zeitpunkt der letzten eingegangenen Sicherung (Snapshot).</summary>
    public static (long Bytes, DateTimeOffset? LastSnapshot, int Snapshots) RepositoryInfo(ArchiveOptions options, string user)
    {
        var dir = options.PathOf("repos", user);
        if (!Directory.Exists(dir))
        {
            return (0, null, 0);
        }

        var bytes = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
        var snapshots = Path.Combine(dir, "snapshots");
        var files = Directory.Exists(snapshots) ? Directory.GetFiles(snapshots) : [];
        DateTimeOffset? last = files.Length == 0 ? null : files.Max(file => new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero));
        return (bytes, last, files.Length);
    }
}
