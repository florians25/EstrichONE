using System.Security.Cryptography;
using System.Text.Json;
using EstrichOne.Hosting.Instances;

namespace EstrichOne.Hosting.Recovery;

/// <summary>Zugangsdaten der ersten Admin-Person einer Instanz (für den Zugangsbrief).</summary>
public sealed record AccessLetter(string CompanyName, string Url, string DisplayName, string UserName, string? Email, string InitialPassword,
    DateTime CreatedAt);

/// <summary>
/// Hält den Zugangsbrief der ersten Admin-Person bis zum Verwerfen im Instanzverzeichnis (<c>zugang.json</c>, nur root lesbar –
/// wie die <c>.env</c> mit den übrigen Geheimnissen der Instanz). Die Konsole speichert das Passwort nicht in ihrer Datenbank.
/// </summary>
public sealed class AccessLetters(InstanceLayout layout)
{
    public const string FileName = "zugang.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private string PathFor(string slug) => Path.Combine(layout.Directory(slug), FileName);

    public bool Exists(string slug) => File.Exists(PathFor(slug));

    public async Task SaveAsync(string slug, AccessLetter letter, CancellationToken cancellationToken)
    {
        var path = PathFor(slug);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(letter, Json), cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public async Task<AccessLetter?> ReadAsync(string slug, CancellationToken cancellationToken)
    {
        var path = PathFor(slug);
        return File.Exists(path) ? JsonSerializer.Deserialize<AccessLetter>(await File.ReadAllTextAsync(path, cancellationToken), Json) : null;
    }

    public void Discard(string slug)
    {
        var path = PathFor(slug);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Gut lesbares Startpasswort (ohne verwechselbare Zeichen), 4×4 Zeichen mit Bindestrichen, ≈ 80 Bit.</summary>
    public static string GeneratePassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var groups = Enumerable.Range(0, 4).Select(_ => new string(Enumerable.Range(0, 4)
            .Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray()));
        return string.Join('-', groups);
    }
}
