using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EstrichOne.Api.Infrastructure.Peers;

/// <summary>Fehler beim Koppeln oder beim Zugriff auf Archivserver/Backup-Ziel; <see cref="Code"/> ist stabil.</summary>
public sealed class PeerException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

public sealed record PeerHello(string Product, string Role, string Name, string Version, string CaCertificatePem, string CaFingerprint,
    bool PairingOpen);

/// <summary>Ergebnis einer Kopplung: gepinnte CA, eigener Zugang, bei Backup-Zielen zusätzlich die restic-Zugangsdaten.</summary>
public sealed record PeerPairing(Uri Address, string Role, string Name, string Version, string CaCertificatePem, string CaFingerprint,
    string ClientId, string Token, string? RestPath, string? RestUsername, string? RestPassword);

internal sealed record PeerPairResponse(string ClientId, string Token, string ServerProof, string Role, string Name, string? RestPath,
    string? RestUsername, string? RestPassword);

/// <summary>
/// Kopplung mit einem EstrichONE-Archivserver oder Backup-Ziel (Protokoll v1, Gegenstück in backend/src/EstrichOne.Archive):
/// 1. Zertifikat und CA abrufen (noch ohne Vertrauen), prüfen, dass das Serverzertifikat von dieser CA stammt.
/// 2. Beide Seiten beweisen per HMAC über den CA-Fingerabdruck, dass sie den Einmal-Code kennen.
/// Danach wird nur noch dieser CA vertraut (Pinning) – ohne Domain, öffentliche CA oder Zertifikatsimport.
/// </summary>
public static class PeerClient
{
    public const int DefaultPort = 8443;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>„192.168.1.50“, „archiv.local:9443“ oder „https://…“ → https-Basisadresse (Standardport 8443).</summary>
    public static Uri NormalizeAddress(string input)
    {
        var text = (input ?? string.Empty).Trim().TrimEnd('/');
        if (text.Length == 0)
        {
            throw new PeerException("PEER_ADDRESS_INVALID", "Bitte die Adresse des Rechners angeben (z. B. 192.168.1.50).");
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
            || uri.PathAndQuery is not ("/" or ""))
        {
            throw new PeerException("PEER_ADDRESS_INVALID", "Die Adresse ist ungültig. Beispiel: 192.168.1.50 oder archiv.firma.local:8443");
        }

        var authority = text[(text.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/')[0];
        var explicitPort = System.Text.RegularExpressions.Regex.IsMatch(authority, @":\d+$");
        var port = explicitPort ? uri.Port : DefaultPort;
        return new UriBuilder(Uri.UriSchemeHttps, uri.Host, port).Uri;
    }

    public static string NormalizeCode(string code) =>
        new([.. (code ?? string.Empty).ToUpperInvariant().Where(c => Alphabet.Contains(c, StringComparison.Ordinal))]);

    public static string Proof(string code, string purpose, string caFingerprint, string nonce)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeCode(code)));
        var data = Encoding.UTF8.GetBytes($"estrichone-pair-v1|{purpose}|{caFingerprint.ToLowerInvariant()}|{nonce}");
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, data));
    }

    public static string Fingerprint(X509Certificate2 certificate) => Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));

    /// <summary>Für die Anzeige: Vierergruppen in Großbuchstaben (wie auf dem Archivrechner ausgegeben).</summary>
    public static string FormatFingerprint(string hex) =>
        string.Join(' ', Enumerable.Range(0, hex.Length / 8).Select(i => hex.Substring(i * 8, 8).ToUpperInvariant()));

    public static async Task<PeerPairing> PairAsync(Uri address, string code, string clientName, string expectedRole,
        CancellationToken cancellationToken)
    {
        if (NormalizeCode(code).Length != 12)
        {
            throw new PeerException("PEER_CODE_INVALID", "Der Kopplungscode hat 12 Zeichen (z. B. ABCD-EFGH-JKLM).");
        }

        X509Certificate2? presented = null;
        using var trustingHandler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                // Schritt 1: Zertifikat nur merken; Vertrauen entsteht erst durch den Code-Beweis.
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    presented = certificate is null ? null : X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                    return certificate is not null;
                },
            },
        };
        using var http = new HttpClient(trustingHandler) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
        PeerHello hello;
        try
        {
            hello = await http.GetFromJsonAsync<PeerHello>("/pair/hello", cancellationToken)
                ?? throw new PeerException("PEER_NOT_ESTRICHONE", "Unter dieser Adresse antwortet kein EstrichONE-Dienst.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new PeerException("PEER_UNREACHABLE",
                $"{address.Host}:{address.Port} ist nicht erreichbar. Läuft der Dienst, ist der Port {address.Port} freigegeben und die Adresse richtig?",
                exception);
        }

        using (presented)
        {
            if (hello.Product != "EstrichONE")
            {
                throw new PeerException("PEER_NOT_ESTRICHONE", "Unter dieser Adresse antwortet kein EstrichONE-Dienst.");
            }

            if (hello.Role != expectedRole)
            {
                throw new PeerException("PEER_WRONG_ROLE", expectedRole == "archive"
                    ? "Unter dieser Adresse läuft ein Backup-Ziel, kein Archivserver."
                    : "Unter dieser Adresse läuft ein Archivserver, kein Backup-Ziel.");
            }

            using var ca = X509Certificate2.CreateFromPem(hello.CaCertificatePem);
            var fingerprint = Fingerprint(ca);
            if (presented is null || !ChainsTo(ca, presented))
            {
                throw new PeerException("PEER_CERTIFICATE_MISMATCH", "Das Zertifikat passt nicht zum Dienst – Verbindung abgebrochen.");
            }

            var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            using var response = await http.PostAsJsonAsync("/pair/complete",
                new { clientName, nonce, proof = Proof(code, "client", fingerprint, nonce), host = address.Host }, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw (int)response.StatusCode switch
                {
                    403 => new PeerException("PEER_CODE_WRONG", "Der Kopplungscode stimmt nicht. Nach 5 Fehlversuchen wird er ungültig."),
                    410 => new PeerException("PEER_PAIRING_CLOSED",
                        "Der Kopplungscode ist abgelaufen oder verbraucht. Auf dem Rechner einen neuen erzeugen: docker exec estrichone-archive estrichone-archive pair"),
                    _ => new PeerException("PEER_PAIRING_FAILED", $"Kopplung fehlgeschlagen (HTTP {(int)response.StatusCode})."),
                };
            }

            var paired = await response.Content.ReadFromJsonAsync<PeerPairResponse>(cancellationToken)
                ?? throw new PeerException("PEER_PAIRING_FAILED", "Kopplung fehlgeschlagen (leere Antwort).");
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Proof(code, "server", fingerprint, nonce)),
                    Encoding.ASCII.GetBytes(paired.ServerProof ?? string.Empty)))
            {
                throw new PeerException("PEER_SERVER_PROOF_INVALID", "Der Dienst konnte den Code nicht bestätigen – möglicher Angriff, Kopplung abgebrochen.");
            }

            return new PeerPairing(address, hello.Role, paired.Name, hello.Version, hello.CaCertificatePem, fingerprint, paired.ClientId,
                paired.Token, paired.RestPath, paired.RestUsername, paired.RestPassword);
        }
    }

    /// <summary>HttpClient, der nur dem beim Koppeln gepinnten CA-Zertifikat vertraut (Hostname egal, z. B. IP im LAN).</summary>
    public static HttpClient CreateClient(Uri address, string caCertificatePem, string token, TimeSpan timeout)
    {
        var ca = X509Certificate2.CreateFromPem(caCertificatePem);
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    if (certificate is null)
                    {
                        return false;
                    }

                    using var leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                    return ChainsTo(ca, leaf);
                },
            },
        };
        var http = new HttpClient(handler, disposeHandler: true) { BaseAddress = address, Timeout = timeout };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    public static bool ChainsTo(X509Certificate2 ca, X509Certificate2 leaf)
    {
        if (leaf.RawData.AsSpan().SequenceEqual(ca.RawData))
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(leaf) && chain.ChainElements[^1].Certificate.RawData.AsSpan().SequenceEqual(ca.RawData);
    }
}
