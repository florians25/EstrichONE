using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Devices;

/// <summary>
/// Einmal-Einrichtungscode aus dem IT Center (docs/reengineering/10, Schritt 2A; 11, Abschnitt 2).
/// Gespeichert wird nur der SHA-256-Hash; der Code ist 15 Minuten gültig und genau einmal einlösbar.
/// </summary>
internal sealed class EnrollmentCode
{
    /// <summary>Ohne leicht verwechselbare Zeichen (0/O, 1/I/L): 32 Zeichen, also 5 Bit je Zeichen.</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public Guid Id { get; private set; }
    public byte[] CodeHash { get; private set; } = [];
    public DeviceKind Kind { get; private set; }
    public string? DeviceName { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public Guid? UsedByDeviceId { get; private set; }

    private EnrollmentCode()
    {
    }

    /// <summary>Erzeugt einen neuen Code im Format <c>XXXX-XXXX</c> (40 Bit Zufall).</summary>
    public static (EnrollmentCode Entity, string Code) Create(DeviceKind kind, string? deviceName, Guid createdByUserId, DateTimeOffset now)
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        var code = $"{chars[..4]}-{chars[4..]}";
        var entity = new EnrollmentCode
        {
            Id = Guid.CreateVersion7(now),
            CodeHash = Hash(code),
            Kind = kind,
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim(),
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
        return (entity, code);
    }

    /// <summary>Groß-/Kleinschreibung, Bindestriche und Leerzeichen spielen beim Abtippen keine Rolle.</summary>
    public static byte[] Hash(string code)
    {
        var normalized = new string(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
    }
}

internal sealed class EnrollmentCodeConfiguration : IEntityTypeConfiguration<EnrollmentCode>
{
    public void Configure(EntityTypeBuilder<EnrollmentCode> builder)
    {
        builder.ToTable("device_enrollment_codes");
        builder.Property(code => code.Id).ValueGeneratedNever();
        builder.Property(code => code.CodeHash).HasMaxLength(32);
        builder.HasIndex(code => code.CodeHash).IsUnique();
        builder.Property(code => code.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(code => code.DeviceName).HasMaxLength(100);
        builder.HasOne<User>().WithMany().HasForeignKey(code => code.CreatedByUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Device>().WithMany().HasForeignKey(code => code.UsedByDeviceId).OnDelete(DeleteBehavior.SetNull);
    }
}
