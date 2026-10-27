using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;

namespace EstrichOne.Api.Infrastructure.Hosting;

/// <summary>
/// ASP.NET Data Protection mit dauerhaft gespeicherten Schlüsseln – für verschlüsselt abgelegte Geheimnisse
/// (SMTP-Passwort, Zugänge zu Archivserver und Backup-Zielen). Die Schlüssel liegen in der Dateiablage
/// (<c>EstrichOne:DataProtection:KeysPath</c>, Standard <c>&lt;Dateiablage&gt;/.keys</c>) und sind damit Teil der Sicherung:
/// Nach einer Wiederherstellung lassen sich die Geheimnisse wieder entschlüsseln.
/// </summary>
public static class DataProtectionSetup
{
    public const string ApplicationName = "EstrichONE";

    public static IServiceCollection AddEstrichOneDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var path = configuration["EstrichOne:DataProtection:KeysPath"]
            ?? Path.Combine(configuration["EstrichOne:Files:RootPath"] ?? "/data/files", ".keys");
        // AES-256-GCM (authentifizierte Verschlüsselung) für neue Schlüssel; ältere Schlüssel (AES-256-CBC + HMAC-SHA256)
        // bleiben lesbar, bis sie nach 90 Tagen turnusgemäß ersetzt sind.
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName)
            .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
            {
                EncryptionAlgorithm = EncryptionAlgorithm.AES_256_GCM,
                ValidationAlgorithm = ValidationAlgorithm.HMACSHA256,
            });
        try
        {
            Directory.CreateDirectory(path);
            builder.PersistKeysToFileSystem(new DirectoryInfo(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Lokale Entwicklung ohne beschreibbares /data: flüchtige Schlüssel (Geheimnisse überleben keinen Neustart).
        }

        return services;
    }
}
