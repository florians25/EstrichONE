using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EstrichOne.Archive;

/// <summary>
/// Kopplungsprotokoll v1 (identisch in der Suite, Modul Archive):
/// Beide Seiten beweisen die Kenntnis des Einmal-Codes per HMAC über den Fingerabdruck der CA, die die Suite
/// gesehen hat. Ein Angreifer dazwischen hätte einen anderen Fingerabdruck – die Beweise passen dann nicht.
/// </summary>
public static class PairingProof
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Normalize(string code) =>
        new([.. code.ToUpperInvariant().Where(c => Alphabet.Contains(c, StringComparison.Ordinal))]);

    public static string Compute(string code, string purpose, string caFingerprint, string nonce)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code)));
        var data = Encoding.UTF8.GetBytes($"estrichone-pair-v1|{purpose}|{caFingerprint.ToLowerInvariant()}|{nonce}");
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, data));
    }

    public static bool Matches(string expected, string? actual) =>
        actual is not null && actual.Length == expected.Length
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));

    /// <summary>12 Zeichen aus 32 (60 Bit), angezeigt als XXXX-XXXX-XXXX.</summary>
    public static string NewCode()
    {
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        var raw = new string(chars);
        return $"{raw[..4]}-{raw[4..8]}-{raw[8..]}";
    }
}

public sealed record PairedClient(
    string Id, string Name, string TokenSha256, DateTimeOffset PairedAt, string? RestUser, string? RestPasswordSha256);

internal sealed record PairingState(string Code, DateTimeOffset ExpiresAt, int FailedAttempts);

public sealed record PairingResult(PairedClient Client, string Token, string? RestPassword);

/// <summary>Gekoppelte Server (Suite-Instanzen bzw. Mandanten) und der aktuelle Kopplungscode – als Dateien im Datenverzeichnis.</summary>
public sealed class ClientRegistry
{
    public const int MaxFailedAttempts = 5;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ArchiveOptions _options;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();

    public ClientRegistry(ArchiveOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
    }

    private string ClientsFile => _options.PathOf("clients.json");
    private string PairingFile => _options.PathOf("pairing.json");

    public IReadOnlyList<PairedClient> Clients
    {
        get
        {
            lock (_gate)
            {
                return File.Exists(ClientsFile)
                    ? JsonSerializer.Deserialize<List<PairedClient>>(File.ReadAllText(ClientsFile), Json) ?? []
                    : [];
            }
        }
    }

    /// <summary>Neuer Code (alter wird ungültig). Wird beim ersten Start und über <c>estrichone-archive pair</c> erzeugt.</summary>
    public (string Code, DateTimeOffset ExpiresAt) NewPairingCode()
    {
        lock (_gate)
        {
            var state = new PairingState(PairingProof.NewCode(), _clock.GetUtcNow() + _options.PairingCodeLifetime, 0);
            WritePrivate(PairingFile, JsonSerializer.Serialize(state, Json));
            return (state.Code, state.ExpiresAt);
        }
    }

    /// <summary>Gültiger offener Code oder null.</summary>
    public (string Code, DateTimeOffset ExpiresAt)? OpenPairing()
    {
        lock (_gate)
        {
            var state = ReadPairing();
            return state is null ? null : (state.Code, state.ExpiresAt);
        }
    }

    public PairingResult? Complete(string clientName, string caFingerprint, string nonce, string? proof, out string? serverProof)
    {
        serverProof = null;
        lock (_gate)
        {
            var state = ReadPairing();
            if (state is null)
            {
                return null;
            }

            if (!PairingProof.Matches(PairingProof.Compute(state.Code, "client", caFingerprint, nonce), proof))
            {
                var failed = state with { FailedAttempts = state.FailedAttempts + 1 };
                if (failed.FailedAttempts >= MaxFailedAttempts)
                {
                    File.Delete(PairingFile);
                }
                else
                {
                    WritePrivate(PairingFile, JsonSerializer.Serialize(failed, Json));
                }

                return null;
            }

            serverProof = PairingProof.Compute(state.Code, "server", caFingerprint, nonce);
            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
            string? restPassword = null;
            string? restUser = null;
            if (_options.Role == ServerRole.BackupTarget)
            {
                restUser = "eo-" + id;
                restPassword = Base64Url(RandomNumberGenerator.GetBytes(24));
            }

            var name = clientName.Trim();
            var client = new PairedClient(id, name.Length is > 0 and <= 120 ? name : "EstrichONE", Sha256(token), _clock.GetUtcNow(),
                restUser, restPassword is null ? null : Sha256(restPassword));
            var clients = Clients.ToList();
            clients.Add(client);
            WritePrivate(ClientsFile, JsonSerializer.Serialize(clients, Json));
            File.Delete(PairingFile);
            return new PairingResult(client, token, restPassword);
        }
    }

    public PairedClient? Authenticate(string? bearerToken)
    {
        if (string.IsNullOrEmpty(bearerToken))
        {
            return null;
        }

        var hash = Sha256(bearerToken);
        return Clients.FirstOrDefault(client => PairingProof.Matches(client.TokenSha256, hash));
    }

    public PairedClient? AuthenticateRest(string user, string password)
    {
        var hash = Sha256(password);
        return Clients.FirstOrDefault(client => client.RestUser == user && client.RestPasswordSha256 is { } expected
            && PairingProof.Matches(expected, hash));
    }

    public bool Remove(string clientId)
    {
        lock (_gate)
        {
            var clients = Clients.ToList();
            var removed = clients.RemoveAll(client => client.Id == clientId) > 0;
            WritePrivate(ClientsFile, JsonSerializer.Serialize(clients, Json));
            return removed;
        }
    }

    private PairingState? ReadPairing()
    {
        if (!File.Exists(PairingFile))
        {
            return null;
        }

        var state = JsonSerializer.Deserialize<PairingState>(File.ReadAllText(PairingFile), Json);
        return state is null || state.ExpiresAt < _clock.GetUtcNow() ? null : state;
    }

    internal static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void WritePrivate(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temp, path, overwrite: true);
    }
}
