using EstrichOne.Hosting.Domain;

namespace EstrichOne.Hosting.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("0.1.0-preview.3", "0.1.0-preview.4", true)]
    [InlineData("0.1.0-preview.10", "0.1.0-preview.4", false)]
    [InlineData("0.1.0-preview.4", "0.1.0", true)]
    [InlineData("0.1.0", "0.1.0-preview.9", false)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.9.9", "0.10.0", true)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", true)]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta", true)]
    [InlineData("kaputt", "0.1.0", true)]
    [InlineData("0.1.0", "kaputt", false)]
    public void IsOlder_VergleichtNachSemVer(string version, string target, bool older) =>
        SemanticVersion.IsOlder(version, target).ShouldBe(older);

    [Fact]
    public void TryParse_ToString_IstUmkehrbar() =>
        SemanticVersion.TryParse("v1.2.3-rc.1+build5")!.ToString().ShouldBe("1.2.3-rc.1");
}
