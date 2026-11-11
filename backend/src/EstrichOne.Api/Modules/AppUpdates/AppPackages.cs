using System.Security.Cryptography;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Hosting;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.AppUpdates;

/// <summary>Beschreibung des Android-Pakets (<c>android.json</c>, erzeugt von .github/workflows/release.yml).</summary>
/// <param name="File">Dateiname des APK im selben Verzeichnis.</param>
/// <param name="Sha256">SHA-256 des APK (hex, klein).</param>
/// <param name="ReleaseSigned">Mit dem Schlüssel des Herausgebers signiert (sonst Testversion mit Debug-Schlüssel).</param>
/// <param name="SignerSha256">SHA-256 des Signaturzertifikats (hex) – die App erkennt damit vorab, ob sie das Update installieren kann.</param>
public sealed record AndroidPackageManifest(string Version, int VersionCode, string File, string Sha256, long Size, bool ReleaseSigned, string? SignerSha256);

/// <summary>Geprüftes Paket: vorhanden und Prüfsumme stimmt (<see cref="Error"/> ist dann <c>null</c>).</summary>
public sealed record AndroidPackage(AndroidPackageManifest Manifest, string Path, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Android-Paket, das dieser Server ausliefert. Wird einmal geprüft (Datei vorhanden, Größe und SHA-256 wie in
/// <c>android.json</c>); ein beschädigtes oder fremdes Paket wird nie ausgeliefert, die Verwaltung zeigt den Grund.
/// </summary>
public sealed class AppPackageCatalog(IOptions<AppUpdateOptions> options, ILogger<AppPackageCatalog> logger)
{
    public const string ManifestFile = "android.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Lock _gate = new();
    private (bool Loaded, AndroidPackage? Package) _android;

    /// <summary>Paket (auch ungültig, mit Fehler) oder <c>null</c>, wenn keines bereitliegt.</summary>
    public AndroidPackage? Android()
    {
        lock (_gate)
        {
            if (!_android.Loaded)
            {
                _android = (true, Load());
            }

            return _android.Package;
        }
    }

    private AndroidPackage? Load()
    {
        var directory = options.Value.PackagePath;
        var manifestPath = Path.Combine(directory, ManifestFile);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        AndroidPackageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AndroidPackageManifest>(File.ReadAllText(manifestPath), Json);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Android-Paketbeschreibung {Path} ist ungültig", manifestPath);
            return null;
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.File) || !VersionComparer.IsValid(manifest.Version))
        {
            logger.LogWarning("Android-Paketbeschreibung {Path} ist unvollständig", manifestPath);
            return null;
        }

        manifest = manifest with { Version = VersionComparer.Normalize(manifest.Version), Sha256 = manifest.Sha256.Trim().ToLowerInvariant() };
        var path = Path.Combine(directory, Path.GetFileName(manifest.File));
        var error = Verify(path, manifest);
        if (error is not null)
        {
            logger.LogWarning("Android-Paket {Path} wird nicht ausgeliefert: {Error}", path, error);
        }

        return new AndroidPackage(manifest, path, error);
    }

    internal static string? Verify(string path, AndroidPackageManifest manifest)
    {
        if (!File.Exists(path))
        {
            return "Die APK-Datei fehlt.";
        }

        using var stream = File.OpenRead(path);
        if (manifest.Size > 0 && stream.Length != manifest.Size)
        {
            return $"Die Dateigröße stimmt nicht ({stream.Length} statt {manifest.Size} Byte).";
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        return hash == manifest.Sha256 ? null : "Die Prüfsumme (SHA-256) stimmt nicht – die Datei ist beschädigt oder ausgetauscht.";
    }
}
