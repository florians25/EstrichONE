using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EstrichOne.Api.Modules.Notifications;

/// <summary>
/// Kryptografie für Pushnachrichten, nur mit .NET-Bordmitteln: Web-Push-Verschlüsselung „aes128gcm“ (RFC 8291/8188),
/// VAPID-Anmeldung (RFC 8292, ES256) und das APNs-Provider-Token (ES256 mit dem .p8-Schlüssel von Apple).
/// </summary>
public static class PushCrypto
{
    private const int RecordSize = 4096;

    public static string Base64Url(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string value)
    {
        var s = value.Trim().Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }

    /// <summary>Neues VAPID-Schlüsselpaar: öffentlicher Schlüssel (65 Byte, unkomprimiert) und privater Skalar (32 Byte), Base64url.</summary>
    public static (string PublicKey, string PrivateKey) CreateVapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        return (Base64Url(Uncompressed(parameters.Q)), Base64Url(parameters.D!));
    }

    /// <summary>Verschlüsselt eine Nachricht für ein Web-Push-Abo (ein Datensatz, RFC 8291).</summary>
    public static byte[] EncryptWebPush(byte[] plaintext, string p256dh, string auth) =>
        EncryptWebPush(plaintext, FromBase64Url(p256dh), FromBase64Url(auth), RandomNumberGenerator.GetBytes(16), null);

    /// <summary>Wie oben, mit festem Salt und Absenderschlüssel (für die Testvektoren aus RFC 8291, Anhang A).</summary>
    internal static byte[] EncryptWebPush(byte[] plaintext, byte[] uaPublic, byte[] authSecret, byte[] salt, ECParameters? senderKey)
    {
        if (uaPublic.Length != 65 || uaPublic[0] != 0x04 || authSecret.Length < 16)
        {
            throw new CryptographicException("Ungültiges Web-Push-Abo (p256dh/auth).");
        }

        if (plaintext.Length > RecordSize - 16 - 1 - 86)
        {
            throw new CryptographicException("Pushnachricht ist zu groß.");
        }

        using var sender = senderKey is { } fixedKey ? ECDiffieHellman.Create(fixedKey) : ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var receiver = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uaPublic[1..33], Y = uaPublic[33..65] },
        });
        var asPublic = Uncompressed(sender.ExportParameters(false).Q);
        var ecdhSecret = sender.DeriveRawSecretAgreement(receiver.PublicKey);

        // IKM = HKDF(auth, ecdh, "WebPush: info\0" || ua_public || as_public, 32)
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), uaPublic, asPublic);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, authSecret, keyInfo);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded, 0);
        padded[^1] = 0x02; // letzter Datensatz
        var cipher = new byte[padded.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Encrypt(nonce, padded, cipher, tag);
        }

        var header = new byte[16 + 4 + 1 + asPublic.Length];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), RecordSize);
        header[20] = (byte)asPublic.Length;
        asPublic.CopyTo(header, 21);
        return Concat(header, cipher, tag);
    }

    /// <summary>VAPID-Kopfzeile <c>Authorization: vapid t=…, k=…</c> für die Herkunft des Push-Endpunkts.</summary>
    public static string VapidAuthorization(Uri endpoint, string publicKey, string privateKey, string subject, DateTimeOffset now)
    {
        var pub = FromBase64Url(publicKey);
        using var key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = FromBase64Url(privateKey),
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] },
        });
        var audience = endpoint.GetLeftPart(UriPartial.Authority);
        var jwt = Jwt(key, new Dictionary<string, object> { ["typ"] = "JWT", ["alg"] = "ES256" },
            new Dictionary<string, object> { ["aud"] = audience, ["exp"] = now.AddHours(12).ToUnixTimeSeconds(), ["sub"] = subject });
        return $"vapid t={jwt}, k={publicKey}";
    }

    /// <summary>APNs-Provider-Token (gültig bis zu 60 Minuten; Apple lehnt häufigeres Erneuern als alle 20 Minuten ab).</summary>
    public static string ApnsToken(string p8Pem, string keyId, string teamId, DateTimeOffset now)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(p8Pem);
        return Jwt(key, new Dictionary<string, object> { ["alg"] = "ES256", ["kid"] = keyId },
            new Dictionary<string, object> { ["iss"] = teamId, ["iat"] = now.ToUnixTimeSeconds() });
    }

    /// <summary>Prüft einen hochgeladenen .p8-Schlüssel (PKCS#8, P-256).</summary>
    public static bool IsValidP8(string pem)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(pem);
            return key.KeySize == 256;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static string Jwt(ECDsa key, Dictionary<string, object> header, Dictionary<string, object> claims)
    {
        var signingInput = $"{Base64Url(JsonSerializer.SerializeToUtf8Bytes(header))}.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims))}";
        var signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static byte[] Uncompressed(ECPoint q) => Concat([0x04], q.X!, q.Y!);

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
