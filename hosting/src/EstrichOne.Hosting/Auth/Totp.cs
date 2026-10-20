using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EstrichOne.Hosting.Auth;

/// <summary>Zeitbasierte Einmalpasswörter nach RFC 6238 (HMAC-SHA1, 30 Sekunden, 6 Ziffern) – kompatibel mit üblichen Authenticator-Apps.</summary>
public static class Totp
{
    public const int Digits = 6;
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string NewSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static long TimeStep(DateTimeOffset now) => now.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

    public static string Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // RFC 6238/4226 schreiben HMAC-SHA1 vor; Authenticator-Apps erwarten es.
        HMACSHA1.HashData(key, counter, hash);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Prüft einen Code (±1 Zeitschritt Toleranz). Gibt den verwendeten Zeitschritt zurück, damit ein Code nicht
    /// zweimal verwendet werden kann (<paramref name="lastUsedStep"/>).
    /// </summary>
    public static long? Verify(string base32Secret, string? code, DateTimeOffset now, long lastUsedStep)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != Digits)
        {
            return null;
        }

        var key = Base32Decode(base32Secret);
        var current = TimeStep(now);
        for (var delta = -1; delta <= 1; delta++)
        {
            var step = current + delta;
            if (step > lastUsedStep && CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(Compute(key, step)), Encoding.ASCII.GetBytes(digits)))
            {
                return step;
            }
        }

        return null;
    }

    public static string ProvisioningUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period=30";

    public static string Base32Encode(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return builder.ToString();
    }

    public static byte[] Base32Decode(string text)
    {
        var clean = text.Trim().TrimEnd('=').Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var bytes = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Ungültiges Base32-Zeichen.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
