using System.Net;

namespace EstrichOne.Api.Infrastructure.Hosting;

/// <summary>
/// Öffentliche Serveradresse für Links und QR-Codes. Ist <see cref="EstrichOneOptions.PublicUrl"/> nicht gesetzt oder
/// zeigt auf localhost (Standard der Installation, z. B. hinter Nginx Proxy Manager), gilt die Adresse, unter der die
/// Anfrage tatsächlich ankam – sonst würden Geräte beim Scannen „localhost“ ansteuern.
/// </summary>
public static class PublicAddress
{
    public static string Resolve(EstrichOneOptions options, HttpRequest request)
    {
        var configured = options.PublicUrl;
        var usable = request.Host.HasValue && !IsLoopback(request.Host.Host);
        if (configured is not null && (!IsLoopback(configured.Host) || !usable))
            return configured.ToString().TrimEnd('/');
        if (!usable)
            return "https://localhost";
        // Schema der Konfiguration bevorzugen: hinter einem TLS-Proxy kommt die Anfrage oft per http an, die Apps brauchen https.
        return $"{configured?.Scheme ?? request.Scheme}://{request.Host}";
    }

    public static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
}
