using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace EstrichOne.Api.Modules.Identity.Passwords;

/// <summary>
/// Argon2id nach OWASP-Empfehlung (m = 19 MiB, t = 2, p = 1). Gespeichert wird das PHC-Format
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>, damit Parameter später erhöht werden können
/// (<see cref="NeedsRehash"/>).
/// </summary>
public sealed class PasswordHasher
{
    private const int MemoryKib = 19_456;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Für unbekannte Konten: gleich lange Rechenzeit, damit die Antwortzeit nichts verrät.</summary>
    private readonly Lazy<string> _dummyHash;

    public PasswordHasher() => _dummyHash = new Lazy<string>(() => Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(16))));

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(password, salt, MemoryKib, Iterations, Parallelism);
        return string.Create(CultureInfo.InvariantCulture,
            $"$argon2id$v=19$m={MemoryKib},t={Iterations},p={Parallelism}${Encode(salt)}${Encode(hash)}");
    }

    public bool Verify(string password, string? encoded)
    {
        if (encoded is null || !TryParse(encoded, out var parameters))
        {
            // Rechenzeit angleichen, auch wenn kein (gültiger) Hash existiert.
            if (TryParse(_dummyHash.Value, out var dummy))
            {
                Compute(password, dummy.Salt, dummy.Memory, dummy.Iterations, dummy.Parallelism);
            }

            return false;
        }

        var actual = Compute(password, parameters.Salt, parameters.Memory, parameters.Iterations, parameters.Parallelism);
        return CryptographicOperations.FixedTimeEquals(actual, parameters.Hash);
    }

    public bool NeedsRehash(string encoded) =>
        !TryParse(encoded, out var parameters)
        || parameters.Memory < MemoryKib || parameters.Iterations < Iterations || parameters.Hash.Length != HashBytes;

    private static byte[] Compute(string password, byte[] salt, int memory, int iterations, int parallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(HashBytes);
    }

    private static bool TryParse(string encoded, out Parameters parameters)
    {
        parameters = default;
        var parts = encoded.Split('$');
        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19")
        {
            return false;
        }

        var values = parts[3].Split(',').Select(pair => pair.Split('=')).ToDictionary(pair => pair[0], pair => pair[^1]);
        if (!values.TryGetValue("m", out var m) || !values.TryGetValue("t", out var t) || !values.TryGetValue("p", out var p)
            || !int.TryParse(m, CultureInfo.InvariantCulture, out var memory)
            || !int.TryParse(t, CultureInfo.InvariantCulture, out var iterations)
            || !int.TryParse(p, CultureInfo.InvariantCulture, out var parallelism))
        {
            return false;
        }

        try
        {
            parameters = new Parameters(memory, iterations, parallelism, Decode(parts[4]), Decode(parts[5]));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=');

    private static byte[] Decode(string value) => Convert.FromBase64String(value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '='));

    private readonly record struct Parameters(int Memory, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);
}
