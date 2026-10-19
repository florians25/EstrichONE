using System.Security.Cryptography;
using System.Text;

namespace EstrichOne.Api.Modules.Identity.Sessions;

/// <summary>256-Bit-Zufallstokens mit Präfix (erkennbar in Secret-Scannern), gespeichert nur als SHA-256 (ADR 0011).</summary>
public static class TokenFactory
{
    public const string AccessPrefix = "eo1_";
    public const string RefreshPrefix = "eor1_";

    public static (string Token, byte[] Hash) Create(string prefix)
    {
        var token = prefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
