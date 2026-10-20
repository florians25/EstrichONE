using EstrichOne.Hosting.Domain;

namespace EstrichOne.Hosting.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("mueller")]
    [InlineData("estrichbau-mueller")]
    [InlineData("abc")]
    [InlineData("firma-2")]
    [InlineData("a1234567890123456789012345678901")]
    public void CheckSlug_GueltigeKuerzel_SindErlaubt(string slug) => Validation.CheckSlug(slug).ShouldBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("Mueller")]
    [InlineData("1firma")]
    [InlineData("firma-")]
    [InlineData("-firma")]
    [InlineData("fir--ma")]
    [InlineData("firma_x")]
    [InlineData("firma.x")]
    [InlineData("müller")]
    [InlineData("a; rm -rf /")]
    [InlineData("$(id)")]
    [InlineData("../etc")]
    [InlineData("firma x")]
    [InlineData("a12345678901234567890123456789012")]
    [InlineData("konsole")]
    [InlineData("proxy")]
    public void CheckSlug_UngueltigeOderReservierteKuerzel_WerdenAbgewiesen(string slug) => Validation.CheckSlug(slug).ShouldNotBeNull();

    [Theory]
    [InlineData("firma.estrichone-hosting.de")]
    [InlineData("estrichone.firma.de")]
    [InlineData("xn--mller-kva.de")]
    [InlineData("a.b")]
    public void CheckDomain_GueltigeDomains_SindErlaubt(string domain) => Validation.CheckDomain(domain).ShouldBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("firma")]
    [InlineData("192.168.1.10")]
    [InlineData("*.firma.de")]
    [InlineData("firma.de/pfad")]
    [InlineData("-firma.de")]
    [InlineData("firma-.de")]
    [InlineData("fir ma.de")]
    [InlineData("firma.de {\n respond 200 }")]
    [InlineData("firma..de")]
    [InlineData("Firma.de")]
    public void CheckDomain_UngueltigeDomains_WerdenAbgewiesen(string domain) => Validation.CheckDomain(domain).ShouldNotBeNull();

    [Theory]
    [InlineData("  HTTPS://Firma.DE/ ", "firma.de")]
    [InlineData("müller.de", "xn--mller-kva.de")]
    [InlineData("estrichone.firma.de.", "estrichone.firma.de")]
    public void NormalizeDomain_LiefertKanonischeForm(string input, string expected) => Validation.NormalizeDomain(input).ShouldBe(expected);

    [Theory]
    [InlineData("Estrichbau Müller GmbH", "estrichbau-mueller")]
    [InlineData("Fußboden & Söhne GmbH & Co. KG", "fussboden-soehne")]
    [InlineData("4 Wände Estrich", "m-4-waende-estrich")]
    [InlineData("A", "axx")]
    [InlineData("Proxy", "proxy-1")]
    public void SuggestSlug_ErzeugtGueltigesKuerzel(string company, string expected)
    {
        var slug = Validation.SuggestSlug(company);
        slug.ShouldBe(expected);
        Validation.CheckSlug(slug).ShouldBeNull();
    }

    [Theory]
    [InlineData("Estrichbau \"Müller\"")]
    [InlineData("Firma $HOME")]
    [InlineData("Firma `id`")]
    [InlineData("Firma \\ X")]
    [InlineData("Firma\nX")]
    public void CheckText_EnvSicher_WeistGefaehrlicheZeichenAb(string value) =>
        Validation.CheckText(value, "Der Firmenname", 120, true, envSafe: true).ShouldNotBeNull();

    [Fact]
    public void CheckText_EnvSicher_ErlaubtNormaleFirmennamen() =>
        Validation.CheckText("Müller's Estrich GmbH & Co. KG (Nord)", "Der Firmenname", 120, true, envSafe: true).ShouldBeNull();

    [Theory]
    [InlineData("0.1.0", true)]
    [InlineData("0.1.0-preview.4", true)]
    [InlineData("1.2", false)]
    [InlineData("latest", false)]
    [InlineData("0.1.0;rm", false)]
    [InlineData("0.1.0 -d", false)]
    public void CheckVersion_PrueftForm(string version, bool valid) => (Validation.CheckVersion(version) is null).ShouldBe(valid);

    [Theory]
    [InlineData("info@firma.de", true)]
    [InlineData("a.b+c@sub.firma.de", true)]
    [InlineData("firma.de", false)]
    [InlineData("a@b", false)]
    public void CheckEmail_PrueftForm(string email, bool valid) => (Validation.CheckEmail(email, true) is null).ShouldBe(valid);
}
