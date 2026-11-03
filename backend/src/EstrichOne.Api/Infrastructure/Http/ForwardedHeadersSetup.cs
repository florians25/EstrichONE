using System.Net;
using EstrichOne.Api.Infrastructure.Hosting;
using Microsoft.AspNetCore.HttpOverrides;

namespace EstrichOne.Api.Infrastructure.Http;

/// <summary>
/// Echte Client-Adresse hinter Caddy bzw. Nginx Proxy Manager (für Sperren und Rate-Limits je IP).
/// <c>X-Forwarded-For</c> wird nur von vertrauenswürdigen Proxys übernommen: private Netze, Loopback und
/// <see cref="EstrichOneOptions.TrustedProxies"/>. Wäre die API (fälschlich) direkt aus dem Internet erreichbar,
/// könnte ein Angreifer so keine fremde Adresse vortäuschen und die Sperre je IP umgehen.
/// </summary>
public static class ForwardedHeadersSetup
{
    private static readonly string[] PrivateNetworks =
        ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "::1/128", "fc00::/7", "fe80::/10"];

    public static void Configure(ForwardedHeadersOptions forwarded, EstrichOneOptions options)
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
        foreach (var network in Networks(options.TrustedProxies))
        {
            forwarded.KnownIPNetworks.Add(network);
        }

        // Hinter einem weiteren Proxy (Nginx Proxy Manager) lautet die Kette „Client, Proxy“. Caddy übernimmt fremde
        // Einträge nur von ESTRICHONE_TRUSTED_PROXIES; im Direktbetrieb steht dort deshalb nur der Client.
        forwarded.ForwardLimit = 2;
    }

    public static IReadOnlyList<System.Net.IPNetwork> Networks(string? extra)
    {
        var list = PrivateNetworks.Select(System.Net.IPNetwork.Parse).ToList();
        foreach (var entry in (extra ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (entry == "private_ranges")
            {
                continue;
            }

            if (System.Net.IPNetwork.TryParse(entry, out var network))
            {
                list.Add(network);
            }
            else if (IPAddress.TryParse(entry, out var address))
            {
                list.Add(new System.Net.IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
            }
        }

        return list;
    }
}
