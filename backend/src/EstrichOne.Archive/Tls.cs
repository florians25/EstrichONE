using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace EstrichOne.Archive;

/// <summary>
/// Eigene kleine Zertifizierungsstelle je Installation: Die Suite pinnt beim Koppeln den Fingerabdruck der CA
/// (geprüft über den Kopplungscode). Das Serverzertifikat wird daraus ausgestellt und bei Bedarf erneuert
/// (neue Adresse, Ablauf) – ohne dass die Kopplung bricht. Funktioniert ohne Domain, auch mit IP-Adresse im LAN.
/// </summary>
public sealed class TlsCertificates
{
    private readonly ArchiveOptions _options;
    private readonly ILogger<TlsCertificates> _logger;
    private readonly Lock _gate = new();
    private X509Certificate2 _ca = null!;
    private X509Certificate2 _leaf = null!;
    private HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public TlsCertificates(ArchiveOptions options, ILogger<TlsCertificates> logger)
    {
        _options = options;
        _logger = logger;
        Directory.CreateDirectory(Dir);
        LoadOrCreate();
    }

    private string Dir => _options.PathOf("tls");

    public X509Certificate2 Current
    {
        get
        {
            lock (_gate)
            {
                return _leaf;
            }
        }
    }

    public string CaPem => _ca.ExportCertificatePem();

    /// <summary>SHA-256 des CA-Zertifikats (DER), Kleinbuchstaben-Hex.</summary>
    public string CaFingerprint => Fingerprint(_ca);

    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));

    /// <summary>Für die Anzeige: AB:CD:… in Vierergruppen.</summary>
    public static string FormatFingerprint(string hex) =>
        string.Join(' ', Enumerable.Range(0, hex.Length / 8).Select(i => hex.Substring(i * 8, 8).ToUpperInvariant()));

    /// <summary>Nimmt eine Adresse ins Serverzertifikat auf (z. B. die beim Koppeln verwendete IP).</summary>
    public void EnsureName(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':')))
        {
            return;
        }

        lock (_gate)
        {
            if (_names.Add(host))
            {
                File.WriteAllText(Path.Combine(Dir, "names.json"), JsonSerializer.Serialize(_names.Order().ToArray()));
                IssueLeaf();
            }
        }
    }

    /// <summary>Erneuert das Serverzertifikat rechtzeitig vor Ablauf.</summary>
    public void RenewIfDue(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_leaf.NotAfter.ToUniversalTime() - now.UtcDateTime < TimeSpan.FromDays(30))
            {
                IssueLeaf();
            }
        }
    }

    private void LoadOrCreate()
    {
        var caCert = Path.Combine(Dir, "ca.pem");
        var caKey = Path.Combine(Dir, "ca.key");
        if (File.Exists(caCert) && File.Exists(caKey))
        {
            _ca = X509Certificate2.CreateFromPem(File.ReadAllText(caCert), File.ReadAllText(caKey));
        }
        else
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest($"CN={Escape(_options.Name)} CA, O=EstrichONE", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            var now = DateTimeOffset.UtcNow;
            using var created = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(40));
            WritePrivate(caKey, key.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(caCert, created.ExportCertificatePem());
            _ca = X509Certificate2.CreateFromPem(File.ReadAllText(caCert), File.ReadAllText(caKey));
            LogCreated(_logger, FormatFingerprint(CaFingerprint));
        }

        var namesFile = Path.Combine(Dir, "names.json");
        if (File.Exists(namesFile))
        {
            _names = new HashSet<string>(JsonSerializer.Deserialize<string[]>(File.ReadAllText(namesFile)) ?? [], StringComparer.OrdinalIgnoreCase);
        }

        foreach (var name in _options.Addresses.Append("localhost").Append("127.0.0.1").Append(Environment.MachineName))
        {
            _names.Add(name);
        }

        var leafCert = Path.Combine(Dir, "server.pem");
        var leafKey = Path.Combine(Dir, "server.key");
        if (File.Exists(leafCert) && File.Exists(leafKey))
        {
            var loaded = X509Certificate2.CreateFromPem(File.ReadAllText(leafCert), File.ReadAllText(leafKey));
            var covered = loaded.Extensions.OfType<X509SubjectAlternativeNameExtension>().SelectMany(san =>
                san.EnumerateDnsNames().Concat(san.EnumerateIPAddresses().Select(ip => ip.ToString())))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_names.All(covered.Contains) && loaded.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(30))
            {
                _leaf = loaded;
                return;
            }

            loaded.Dispose();
        }

        IssueLeaf();
    }

    private void IssueLeaf()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={Escape(_options.Name)}, O=EstrichONE", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var name in _names)
        {
            if (IPAddress.TryParse(name, out var ip))
            {
                san.AddIpAddress(ip);
            }
            else
            {
                san.AddDnsName(name);
            }
        }

        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(_ca, true, false));
        var now = DateTimeOffset.UtcNow;
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7f;
        using var issued = request.Create(_ca, now.AddDays(-1), now.AddDays(397), serial);
        var certPem = issued.ExportCertificatePem();
        var keyPem = key.ExportPkcs8PrivateKeyPem();
        WritePrivate(Path.Combine(Dir, "server.key"), keyPem);
        File.WriteAllText(Path.Combine(Dir, "server.pem"), certPem);
        var previous = _leaf;
        _leaf = X509Certificate2.CreateFromPem(certPem, keyPem);
        previous?.Dispose();
    }

    private static void WritePrivate(string path, string content)
    {
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static string Escape(string value) => new([.. value.Where(c => c is not (',' or '=' or '+' or '"' or '\\' or '<' or '>' or ';'))]);

    private static void LogCreated(ILogger logger, string fingerprint) =>
        logger.LogInformation("Neue Zertifizierungsstelle angelegt. Fingerabdruck: {Fingerprint}", fingerprint);
}
