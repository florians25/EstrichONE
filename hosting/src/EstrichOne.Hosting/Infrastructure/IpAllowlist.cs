using System.Net;

namespace EstrichOne.Hosting.Infrastructure;

/// <summary>
/// Optionale IP-Allowlist der Konsole (<c>Hosting__AllowedNetworks</c>, z. B. <c>203.0.113.7/32, 10.0.0.0/8</c>):
/// Ist sie gesetzt, beantwortet die Konsole Anfragen anderer Adressen nur mit 403 – auch Anmeldung und Oberfläche.
/// Ausgewertet wird die Client-Adresse nach <c>UseForwardedHeaders</c> (also hinter dem eigenen Proxy die echte Adresse).
/// Ausgenommen sind nur <c>/health</c> (Container-Healthcheck, ohne Daten) und Aufrufe von der Loopback-Adresse
/// (Notzugang über SSH-Tunnel auf 127.0.0.1:8099).
/// </summary>
public sealed class IpAllowlist
{
    private readonly IReadOnlyList<IPNetwork> networks;

    private IpAllowlist(IReadOnlyList<IPNetwork> networks) => this.networks = networks;

    public bool Enabled => networks.Count > 0;

    /// <summary>Liest die Liste (Komma, Semikolon oder Leerzeichen getrennt); einzelne Adressen gelten als /32 bzw. /128.</summary>
    /// <exception cref="FormatException">Ein Eintrag ist keine gültige Adresse bzw. kein gültiges Netz.</exception>
    public static IpAllowlist Parse(string? value)
    {
        var list = new List<IPNetwork>();
        foreach (var entry in (value ?? string.Empty).Split([',', ';', ' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPNetwork.TryParse(entry, out var network))
            {
                list.Add(network);
            }
            else if (IPAddress.TryParse(entry, out var address))
            {
                list.Add(new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
            }
            else
            {
                throw new FormatException($"Hosting:AllowedNetworks: „{entry}“ ist keine gültige IP-Adresse bzw. kein Netz (z. B. 203.0.113.7 oder 10.0.0.0/8).");
            }
        }

        return new IpAllowlist(list);
    }

    public bool IsAllowed(IPAddress? address)
    {
        if (!Enabled)
        {
            return true;
        }

        if (address is null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address) || networks.Any(n => n.Contains(address));
    }

    public static IApplicationBuilder UseIpAllowlist(IApplicationBuilder app, IpAllowlist allowlist)
    {
        if (!allowlist.Enabled)
        {
            return app;
        }

        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase) || allowlist.IsAllowed(context.Connection.RemoteIpAddress))
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { title = "Nicht erlaubt", status = 403, detail = "Zugriff auf die Konsole ist von dieser Adresse nicht freigegeben." },
                options: null, contentType: "application/problem+json");
        });
    }
}
