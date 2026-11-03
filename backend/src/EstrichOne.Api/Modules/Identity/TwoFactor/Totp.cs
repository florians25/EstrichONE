using System.Security.Cryptography;
using System.Text;

namespace EstrichOne.Api.Modules.Identity.TwoFactor;

/// <summary>
/// Zeitbasierte Einmalcodes nach RFC 6238 (HMAC-SHA1, 6 Ziffern, 30 Sekunden) – kompatibel mit allen gängigen
/// Authenticator-Apps (Microsoft/Google Authenticator, 1Password, Bitwarden, Apple Passwörter …).
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int PeriodSeconds = 30;

    /// <summary>Erlaubte Abweichung der Uhr in Zeitschritten (je 30 s) vor und zurück.</summary>
    public const int AllowedDrift = 1;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long TimeStep(DateTimeOffset time) => time.ToUnixTimeSeconds() / PeriodSeconds;

    public static string Code(byte[] secret, long timeStep)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);
        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // RFC 6238/4226 schreibt HMAC-SHA1 vor; die Sicherheit beruht nicht auf Kollisionsfreiheit.
        HMACSHA1.HashData(secret, counter, hash);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Prüft den Code im Zeitfenster ±<see cref="AllowedDrift"/>. Liefert den getroffenen Zeitschritt; Schritte bis
    /// einschließlich <paramref name="lastUsedStep"/> gelten als verbraucht (kein zweites Einlösen desselben Codes).
    /// </summary>
    public static long? Verify(byte[] secret, string code, DateTimeOffset now, long lastUsedStep)
    {
        var normalized = Normalize(code);
        if (normalized.Length != Digits)
        {
            return null;
        }

        var current = TimeStep(now);
        long? match = null;
        for (var step = current - AllowedDrift; step <= current + AllowedDrift; step++)
        {
            // Alle Schritte rechnen (gleiche Laufzeit), Vergleich zeitkonstant.
            var expected = Encoding.ASCII.GetBytes(Code(secret, step));
            if (CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(normalized)) && step > lastUsedStep)
            {
                match ??= step;
            }
        }

        return match;
    }

    /// <summary>Leerzeichen und Bindestriche („123 456“) entfernen.</summary>
    public static string Normalize(string code) => new([.. code.Where(c => c is not (' ' or '-'))]);

    public static string OtpAuthUri(string issuer, string accountName, byte[] secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        return $"otpauth://totp/{label}?secret={Base32.Encode(secret)}&issuer={Uri.EscapeDataString(issuer)}"
            + $"&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
    }
}

/// <summary>Base32 nach RFC 4648 ohne Auffüllzeichen (Format der Authenticator-Apps).</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return builder.ToString();
    }

    public static byte[] Decode(string text)
    {
        var clean = text.Trim().TrimEnd('=').Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Kein Base32.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
