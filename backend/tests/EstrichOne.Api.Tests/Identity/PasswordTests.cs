using EstrichOne.Api.Modules.Identity.Passwords;

namespace EstrichOne.Api.Tests.Identity;

public sealed class PasswordTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesPhcStringThatVerifies()
    {
        var hash = _hasher.Hash("Belegreif-2026");

        hash.ShouldStartWith("$argon2id$v=19$m=19456,t=2,p=1$");
        _hasher.Verify("Belegreif-2026", hash).ShouldBeTrue();
        _hasher.Verify("belegreif-2026", hash).ShouldBeFalse();
        _hasher.NeedsRehash(hash).ShouldBeFalse();
    }

    [Fact]
    public void Hash_UsesRandomSalt()
    {
        _hasher.Hash("Belegreif-2026").ShouldNotBe(_hasher.Hash("Belegreif-2026"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein-hash")]
    [InlineData("$argon2id$v=19$m=x,t=2,p=1$AAAA$AAAA")]
    public void Verify_WithMissingOrBrokenHash_ReturnsFalse(string? stored) =>
        _hasher.Verify("irgendwas", stored).ShouldBeFalse();

    [Fact]
    public void NeedsRehash_ForWeakerParameters_IsTrue()
    {
        var weak = _hasher.Hash("Belegreif-2026").Replace("m=19456,t=2", "m=4096,t=1", StringComparison.Ordinal);
        _hasher.NeedsRehash(weak).ShouldBeTrue();
    }

    [Theory]
    [InlineData("kurz", "zu wenig Zeichen")]
    [InlineData("passwort123", "verbreitet")]
    [InlineData("aaaaaaaaaaaaaa", "verschiedenen")]
    [InlineData("m.huber-2026-xyz", "Anmeldenamen")]
    public void Policy_RejectsWeakPasswords(string password, string reasonFragment)
    {
        var reason = PasswordPolicy.Validate(password, "m.huber", "max@example.org");

        reason.ShouldNotBeNull();
        if (reasonFragment != "zu wenig Zeichen")
        {
            reason.ShouldContain(reasonFragment);
        }
    }

    [Fact]
    public void Policy_AcceptsLongPassphrase() =>
        PasswordPolicy.Validate("Estrich trocknet langsam 7", "m.huber", null).ShouldBeNull();
}
