using EstrichOne.Api.Modules.Identity.Passwords;

namespace EstrichOne.Api.Tests.Identity;

/// <summary>Passwortregeln: mindestens 12 Zeichen, keine verbreiteten/geleakten Passwörter, auch nicht „verziert“.</summary>
public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("Kurz-2026!")] // 10 Zeichen
    [InlineData("Elf-Zeichen")] // 11 Zeichen
    [InlineData("passwort1234")] // Liste
    [InlineData("Passwort2026!")] // Wort + Ziffern + Zeichen
    [InlineData("P@ssw0rd2026!")] // Leetspeak
    [InlineData("!!Sommer2026!!")]
    [InlineData("iloveyou1234")]
    [InlineData("qwertzuiop12")]
    [InlineData("sommersommer")] // Wort doppelt
    [InlineData("abcabcabcabc")] // Wiederholung
    [InlineData("123456789012")] // Folge
    [InlineData("m.huber-geheim")] // enthält den Anmeldenamen
    [InlineData("aaaaaaaabbbb")] // zu wenige verschiedene Zeichen
    public void Validate_RejectsWeakPasswords(string password) =>
        PasswordPolicy.Validate(password, "m.huber", "m.huber@example.org").ShouldNotBeNull();

    [Theory]
    [InlineData("Estrich trocknet langsam 7")]
    [InlineData("Estrich-Test-2026!")]
    [InlineData("Blau-Kelle-Wolke-42")]
    [InlineData("vT9#qL2m!xR4")]
    public void Validate_AcceptsStrongPasswords(string password) =>
        PasswordPolicy.Validate(password, "m.huber", "m.huber@example.org").ShouldBeNull();

    [Fact]
    public void CommonPasswordList_IsEmbedded() => CommonPasswords.Count.ShouldBeGreaterThan(45_000);
}
