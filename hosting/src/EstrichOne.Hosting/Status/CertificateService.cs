using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Status;

/// <summary>
/// Zertifikat und DNS einer Domain. <c>State</c>: <c>valid</c>, <c>expiring</c> (unter 14 Tagen), <c>invalid</c>
/// (abgelaufen, falscher Name, nicht vertrauenswürdig), <c>missing</c> (noch kein Zertifikat/Handshake scheitert),
/// <c>off</c> (HTTPS ausgeschaltet), <c>unknown</c> (noch nicht geprüft).
/// </summary>
public sealed record CertificateInfo(string Domain, DateTime CheckedAt, string State, DateTime? NotAfter, string? Issuer, int? DaysLeft,
    IReadOnlyList<string> Addresses, string? Error);

/// <summary>
/// Prüft die Zertifikate aller Domains real per TLS-Handshake: beim mitgelieferten Caddy direkt am Proxy-Container mit
/// SNI (unabhängig vom DNS), bei externem Proxy über die öffentliche Adresse. Zusätzlich DNS-Auflösung der Domain.
/// </summary>
public sealed partial class CertificateService(IOptions<HostingOptions> options, TimeProvider clock, ILogger<CertificateService> logger)
{
    public static readonly TimeSpan ExpiringThreshold = TimeSpan.FromDays(14);
    private readonly ConcurrentDictionary<string, CertificateInfo> cache = new(StringComparer.OrdinalIgnoreCase);

    private HostingOptions Settings => options.Value;

    public CertificateInfo? Get(string domain) => cache.GetValueOrDefault(domain);

    public async Task<CertificateInfo> CheckAsync(string domain, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var addresses = await ResolveAsync(domain, cancellationToken);
        CertificateInfo info;
        if (Settings.IsFake)
        {
            var notAfter = now.Date.AddDays(45 + Math.Abs(StringComparer.Ordinal.GetHashCode(domain) % 40));
            info = new CertificateInfo(domain, now, "valid", notAfter, "Let's Encrypt R11 (Simulation)", (int)(notAfter - now).TotalDays,
                addresses.Count > 0 ? addresses : ["203.0.113.10 (Simulation)"], null);
        }
        else if (string.Equals(Settings.Tls, "off", StringComparison.OrdinalIgnoreCase) && Settings.ManagesProxy)
        {
            info = new CertificateInfo(domain, now, "off", null, null, null, addresses, "HTTPS ist in der Konsole ausgeschaltet (HOSTING_TLS=off).");
        }
        else
        {
            info = await HandshakeAsync(domain, Settings.ManagesProxy ? Settings.ProxyContainer : domain, addresses, now, cancellationToken);
        }

        cache[domain] = info;
        return info;
    }

    public void Forget(string domain) => cache.TryRemove(domain, out _);

    private static async Task<IReadOnlyList<string>> ResolveAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var addresses = await Dns.GetHostAddressesAsync(domain, timeout.Token);
            return addresses.Select(a => a.ToString()).Distinct().Take(6).ToList();
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or ArgumentException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }
    }

    private async Task<CertificateInfo> HandshakeAsync(string domain, string host, IReadOnlyList<string> addresses, DateTime now,
        CancellationToken cancellationToken)
    {
        X509Certificate2? certificate = null;
        var errors = SslPolicyErrors.None;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, 443, timeout.Token);
            await using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, policyErrors) =>
            {
                // Nur lesen, nichts übertragen: Zertifikat und Prüfergebnis merken (ungültige Zertifikate brechen den Handshake ab).
                certificate = cert is null ? null : new X509Certificate2(cert);
                errors = policyErrors;
                return policyErrors == SslPolicyErrors.None;
            });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = domain }, timeout.Token);
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (certificate is null)
            {
                LogHandshakeFailed(logger, exception, domain);
                return new CertificateInfo(domain, now, "missing", null, null, null, addresses,
                    exception is OperationCanceledException ? "Zeitüberschreitung beim TLS-Handshake." : $"Kein Zertifikat: {exception.Message}");
            }
        }

        using (certificate)
        {
            if (certificate is null)
            {
                return new CertificateInfo(domain, now, "missing", null, null, null, addresses, "Kein Zertifikat erhalten.");
            }

            var notAfter = certificate.NotAfter.ToUniversalTime();
            var days = (int)Math.Floor((notAfter - now).TotalDays);
            var issuer = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true);
            var internalCa = string.Equals(Settings.Tls, "internal", StringComparison.OrdinalIgnoreCase);
            // Eigene CA (HOSTING_TLS=internal) ist nie öffentlich vertrauenswürdig – dort zählt nur Name und Laufzeit.
            var relevant = internalCa ? errors & ~SslPolicyErrors.RemoteCertificateChainErrors : errors;
            var state = notAfter <= now ? "invalid"
                : relevant != SslPolicyErrors.None ? "invalid"
                : notAfter - now < ExpiringThreshold ? "expiring"
                : "valid";
            var error = state switch
            {
                "invalid" when notAfter <= now => "Zertifikat ist abgelaufen.",
                "invalid" when relevant.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) => "Zertifikat gilt nicht für diese Domain.",
                "invalid" => "Zertifikat ist nicht vertrauenswürdig.",
                "expiring" => $"Zertifikat läuft in {days} Tagen ab.",
                _ => null,
            };
            return new CertificateInfo(domain, now, state, notAfter, issuer, days, addresses, error);
        }
    }
}

public sealed partial class CertificateService
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "TLS-Prüfung von {Domain} fehlgeschlagen.")]
    private static partial void LogHandshakeFailed(ILogger logger, Exception exception, string domain);
}

/// <summary>Prüft stündlich alle Domains (Instanzen und Konsole).</summary>
public sealed class CertificateMonitor(IServiceScopeFactory scopes, CertificateService certificates, IOptions<HostingOptions> options,
    ILogger<CertificateMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                List<string> domains;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
                    domains = await db.Tenants.AsNoTracking().Where(t => t.State != TenantState.Deleted && t.State != TenantState.Deleting)
                        .Select(t => t.Domain).ToListAsync(stoppingToken);
                }

                domains.Add(options.Value.ConsoleDomain);
                await Parallel.ForEachAsync(domains, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = stoppingToken },
                    async (domain, token) => await certificates.CheckAsync(domain, token));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Zertifikatsprüfung fehlgeschlagen.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
