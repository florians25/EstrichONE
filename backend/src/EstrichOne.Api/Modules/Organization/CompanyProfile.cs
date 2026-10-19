using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Organization;

/// <summary>Firmendaten des Betriebs. Genau eine Zeile je Installation (ADR 0004).</summary>
public sealed class CompanyProfile : IVersionedEntity
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public string Name { get; private set; } = string.Empty;
    public string? Street { get; private set; }
    public string? PostalCode { get; private set; }
    public string? City { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private CompanyProfile()
    {
    }

    public static CompanyProfile Create(string name) => new() { Name = name.Trim() };

    public void Update(string name, string? street, string? postalCode, string? city, string? phone, string? email, string? website)
    {
        Name = name.Trim();
        Street = street;
        PostalCode = postalCode;
        City = city;
        Phone = phone;
        Email = email;
        Website = website;
    }
}

internal sealed class CompanyProfileConfiguration : IEntityTypeConfiguration<CompanyProfile>
{
    public void Configure(EntityTypeBuilder<CompanyProfile> builder)
    {
        builder.ToTable("company_profile", table =>
            table.HasCheckConstraint("ck_company_profile_singleton", $"id = {CompanyProfile.SingletonId}"));
        builder.ConfigureVersioned();
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.Property(profile => profile.Name).HasMaxLength(200);
        builder.Property(profile => profile.Street).HasMaxLength(200);
        builder.Property(profile => profile.PostalCode).HasMaxLength(10);
        builder.Property(profile => profile.City).HasMaxLength(100);
        builder.Property(profile => profile.Phone).HasMaxLength(50);
        builder.Property(profile => profile.Email).HasMaxLength(320);
        builder.Property(profile => profile.Website).HasMaxLength(200);
    }
}
