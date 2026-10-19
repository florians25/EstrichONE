using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Setup;

/// <summary>
/// Einmal-Code für die Ersteinrichtung. Gespeichert wird nur der Hash. Der Klartext erscheint einmalig im
/// Server-Log (<c>docker compose logs api</c>) und ist damit nur für den Serverbetreiber sichtbar.
/// Es gibt keine Standardpasswörter und keinen offenen Installationspfad (docs/reengineering/17).
/// </summary>
public sealed class SetupCode
{
    public const int SingletonId = 1;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // ohne leicht verwechselbare Zeichen

    public int Id { get; private set; } = SingletonId;
    public byte[] CodeHash { get; private set; } = [];
    public DateTimeOffset ExpiresAt { get; private set; }
    public int FailedAttempts { get; private set; }

    private SetupCode()
    {
    }

    public static (SetupCode Entity, string Code) Issue(DateTimeOffset now, TimeSpan lifetime)
    {
        var code = Generate();
        return (new SetupCode { CodeHash = Hash(code), ExpiresAt = now + lifetime }, code);
    }

    public void Replace(string code, DateTimeOffset expiresAt)
    {
        CodeHash = Hash(code);
        ExpiresAt = expiresAt;
        FailedAttempts = 0;
    }

    public bool Matches(string candidate, DateTimeOffset now) =>
        ExpiresAt > now && FailedAttempts < 10
        && CryptographicOperations.FixedTimeEquals(Hash(candidate), CodeHash);

    public void RecordFailure() => FailedAttempts++;

    public static string Generate()
    {
        var chars = RandomNumberGenerator.GetItems<char>(Alphabet, 12);
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}-{new string(chars, 8, 4)}";
    }

    private static byte[] Hash(string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal)));
}

internal sealed class SetupCodeConfiguration : IEntityTypeConfiguration<SetupCode>
{
    public void Configure(EntityTypeBuilder<SetupCode> builder)
    {
        builder.ToTable("setup_code", table => table.HasCheckConstraint("ck_setup_code_singleton", $"id = {SetupCode.SingletonId}"));
        builder.Property(code => code.Id).ValueGeneratedNever();
        builder.Property(code => code.CodeHash).HasMaxLength(32);
    }
}
