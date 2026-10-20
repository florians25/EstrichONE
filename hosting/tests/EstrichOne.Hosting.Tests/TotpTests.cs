using System.Text;
using EstrichOne.Hosting.Auth;

namespace EstrichOne.Hosting.Tests;

public sealed class TotpTests
{
    private static readonly byte[] RfcKey = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    // RFC 6238, Anhang B (SHA1) – letzte sechs Ziffern der achtstelligen Referenzwerte.
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Compute_EntsprichtRfc6238(long unixSeconds, string expected) =>
        Totp.Compute(RfcKey, Totp.TimeStep(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))).ShouldBe(expected);

    [Fact]
    public void Verify_AkzeptiertAktuellenCodeNurEinmal()
    {
        var secret = Totp.Base32Encode(RfcKey);
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111111);
        var step = Totp.Verify(secret, "050471", now, lastUsedStep: 0);
        step.ShouldNotBeNull();
        Totp.Verify(secret, "050471", now, step.Value).ShouldBeNull();
    }

    [Fact]
    public void Verify_ToleriertEinenZeitschrittAbweichung()
    {
        var secret = Totp.Base32Encode(RfcKey);
        var now = DateTimeOffset.FromUnixTimeSeconds(1111111111 + 30);
        Totp.Verify(secret, "050 471", now, 0).ShouldNotBeNull();
        Totp.Verify(secret, "050471", now.AddSeconds(60), 0).ShouldBeNull();
        Totp.Verify(secret, "12345", now, 0).ShouldBeNull();
    }

    [Fact]
    public void Base32_IstUmkehrbar()
    {
        var secret = Totp.NewSecret();
        secret.Length.ShouldBe(32);
        Totp.Base32Encode(Totp.Base32Decode(secret)).ShouldBe(secret);
        Totp.Base32Encode(Encoding.ASCII.GetBytes("foobar")).ShouldBe("MZXW6YTBOI");
    }

    [Fact]
    public void ProvisioningUri_EnthaeltAlleAngaben() =>
        Totp.ProvisioningUri("EstrichONE Hosting", "admin", "ABC").ShouldBe(
            "otpauth://totp/EstrichONE%20Hosting:admin?secret=ABC&issuer=EstrichONE%20Hosting&algorithm=SHA1&digits=6&period=30");
}
