using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Devices;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");
        builder.ConfigureVersioned();
        builder.Property(device => device.Id).ValueGeneratedNever();
        builder.Property(device => device.Name).HasMaxLength(100);
        builder.Property(device => device.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(device => device.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(device => device.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(device => device.Model).HasMaxLength(100);
        builder.Property(device => device.OsVersion).HasMaxLength(50);
        builder.Property(device => device.AppVersion).HasMaxLength(50);
        builder.Property(device => device.PublicKey).HasMaxLength(200);
        builder.HasOne<User>().WithMany().HasForeignKey(device => device.AssignedUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(device => device.RegisteredByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(device => device.ApprovedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(device => device.Status);
    }
}

/// <summary>Abschnitt <c>EstrichOne:Devices</c>.</summary>
public sealed class DeviceSettings
{
    public const string SectionName = "EstrichOne:Devices";

    /// <summary>Begrenzt das Durchprobieren von Einrichtungscodes (je IP-Adresse).</summary>
    public const string RateLimitPolicy = "device-registration";

    /// <summary>
    /// Persönliche Geräte von Benutzern mit <c>devices.self_enroll</c> sofort freigeben (Standard).
    /// Poolgeräte warten immer auf einen Administrator (docs/reengineering/11, Abschnitt 2).
    /// </summary>
    public bool AutoApprovePersonalDevices { get; set; } = true;

    /// <summary>Registrierungen je IP-Adresse und Minute. Nur für Testumgebungen erhöhen.</summary>
    public int RegistrationsPerMinutePerAddress { get; set; } = 10;
}
