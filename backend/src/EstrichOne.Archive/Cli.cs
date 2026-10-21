using System.Net.Http.Json;
using System.Text.Json;

namespace EstrichOne.Archive;

/// <summary>
/// Befehle im laufenden Container, z. B. <c>docker exec estrichone-archive estrichone-archive pair</c>:
/// pair (neuer Kopplungscode), status, verify (Prüfprotokoll), export &lt;datei.zip&gt; [client-id], healthcheck.
/// </summary>
internal static class Cli
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> RunAsync(string command, string[] args, ArchiveOptions options)
    {
        switch (command)
        {
            case "pair":
            {
                var (code, expires) = new ClientRegistry(options, TimeProvider.System).NewPairingCode();
                Console.WriteLine($"Kopplungscode: {code}   (gültig bis {expires.ToLocalTime():dd.MM.yyyy HH:mm}, einmal verwendbar)");
                return 0;
            }

            case "healthcheck":
            {
                using var http = Local(options, null);
                try
                {
                    using var response = await http.GetAsync(new Uri("/health", UriKind.Relative));
                    return response.IsSuccessStatusCode ? 0 : 1;
                }
                catch (HttpRequestException)
                {
                    return 1;
                }
            }

            case "status":
            case "verify":
            {
                using var http = Local(options, AdminTokenValue(options));
                using var response = command == "status"
                    ? await http.GetAsync(new Uri("/admin/status", UriKind.Relative))
                    : await http.PostAsync(new Uri("/admin/verify", UriKind.Relative), null);
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                Console.WriteLine(JsonSerializer.Serialize(json, Pretty));
                return response.IsSuccessStatusCode && (command == "status" || json.GetProperty("ok").GetBoolean()) ? 0 : 1;
            }

            case "export":
            {
                var target = args.Length > 0 ? args[0] : options.PathOf($"export-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                using var http = Local(options, AdminTokenValue(options));
                var query = args.Length > 1 ? $"?client={Uri.EscapeDataString(args[1])}" : string.Empty;
                using var response = await http.GetAsync(new Uri("/admin/export" + query, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"Export fehlgeschlagen: {(int)response.StatusCode}");
                    return 1;
                }

                await using (var file = File.Create(target))
                {
                    await response.Content.CopyToAsync(file);
                }

                Console.WriteLine($"Export geschrieben: {target}  (im Container; auf dem Rechner im eingebundenen Datenordner)");
                return 0;
            }

            default:
                Console.Error.WriteLine("Befehle: pair | status | verify | export [datei.zip] [client-id] | healthcheck");
                return 2;
        }
    }

    private static string AdminTokenValue(ArchiveOptions options) => File.ReadAllText(options.PathOf("admin.token")).Trim();

    /// <summary>Nur 127.0.0.1 im selben Container; das eigene Zertifikat wird hier nicht geprüft.</summary>
    private static HttpClient Local(ArchiveOptions options, string? token)
    {
#pragma warning disable CA5359, CA2000 // Verbindung ausschließlich zur eigenen Loopback-Adresse
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true, UseProxy = false };
#pragma warning restore CA5359, CA2000
        var http = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{options.Port}"), Timeout = TimeSpan.FromHours(2) };
        if (token is not null)
        {
            http.DefaultRequestHeaders.Add("X-Admin-Token", token);
        }

        return http;
    }
}
