using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Bauakten;

internal sealed class BauakteConfiguration : IEntityTypeConfiguration<Bauakte>
{
    public void Configure(EntityTypeBuilder<Bauakte> builder)
    {
        builder.ToTable("bauakten");
        builder.ConfigureVersioned();
        builder.Property(bauakte => bauakte.Id).ValueGeneratedNever();
        builder.Property(bauakte => bauakte.Number).HasMaxLength(20);
        builder.Property(bauakte => bauakte.Title).HasMaxLength(Bauakte.MaxTitleLength);
        builder.Property(bauakte => bauakte.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(bauakte => bauakte.Street).HasMaxLength(200);
        builder.Property(bauakte => bauakte.PostalCode).HasMaxLength(10);
        builder.Property(bauakte => bauakte.City).HasMaxLength(100);
        builder.Property(bauakte => bauakte.Description).HasMaxLength(4000);
        builder.Property(bauakte => bauakte.TemplateKey).HasMaxLength(50);

        builder.HasIndex(bauakte => bauakte.Number).IsUnique().HasFilter("number IS NOT NULL");
        builder.HasIndex(bauakte => bauakte.Status);
        builder.HasIndex(bauakte => bauakte.CustomerId);
        builder.HasIndex(bauakte => bauakte.UpdatedAt);

        builder.HasOne<Contact>().WithMany().HasForeignKey(bauakte => bauakte.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(bauakte => bauakte.ResponsibleUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(bauakte => bauakte.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(bauakte => bauakte.Sections).WithOne().HasForeignKey(section => section.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(bauakte => bauakte.Members).WithOne().HasForeignKey(member => member.BauakteId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BauakteSectionConfiguration : IEntityTypeConfiguration<BauakteSection>
{
    public void Configure(EntityTypeBuilder<BauakteSection> builder)
    {
        builder.ToTable("bauakte_sections");
        builder.Property(section => section.Id).ValueGeneratedNever();
        builder.Property(section => section.Name).HasMaxLength(100);
    }
}

internal sealed class BauakteMemberConfiguration : IEntityTypeConfiguration<BauakteMember>
{
    public void Configure(EntityTypeBuilder<BauakteMember> builder)
    {
        builder.ToTable("bauakte_members");
        builder.HasKey(member => new { member.BauakteId, member.UserId });
        builder.Property(member => member.Role).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<User>().WithMany().HasForeignKey(member => member.UserId).OnDelete(DeleteBehavior.Cascade);
        // Zugriffsprüfung „meine Bauakten“ fragt nach Benutzer.
        builder.HasIndex(member => member.UserId);
    }
}

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms", table =>
        {
            table.HasCheckConstraint("ck_rooms_area_not_negative", "area_square_meters IS NULL OR area_square_meters >= 0");
            table.HasCheckConstraint("ck_rooms_thickness_positive", "thickness_millimeters IS NULL OR thickness_millimeters > 0");
        });
        builder.ConfigureVersioned();
        builder.Property(room => room.Id).ValueGeneratedNever();
        builder.Property(room => room.Name).HasMaxLength(100);
        builder.Property(room => room.Floor).HasMaxLength(50);
        builder.Property(room => room.AreaSquareMeters).HasPrecision(10, 2);
        builder.Property(room => room.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(room => room.Notes).HasMaxLength(2000);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(room => room.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<BauakteSection>().WithMany().HasForeignKey(room => room.SectionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ScreedType>().WithMany().HasForeignKey(room => room.ScreedTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(room => room.BauakteId);
    }
}

internal sealed class ScreedTypeConfiguration : IEntityTypeConfiguration<ScreedType>
{
    public void Configure(EntityTypeBuilder<ScreedType> builder)
    {
        builder.ToTable("screed_types");
        builder.ConfigureVersioned();
        builder.Property(type => type.Id).ValueGeneratedNever();
        builder.Property(type => type.Name).HasMaxLength(100);
        builder.Property(type => type.Designation).HasMaxLength(50);
        builder.Property(type => type.Binder).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(type => type.Name).IsUnique();
    }
}

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("notes");
        builder.ConfigureVersioned();
        builder.Property(note => note.Id).ValueGeneratedNever();
        builder.Property(note => note.Text).HasMaxLength(Note.MaxTextLength);
        builder.Property(note => note.Color).HasMaxLength(20);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(note => note.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(note => note.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(note => new { note.BauakteId, note.CreatedAt });
    }
}

/// <summary>Nummernkreis je Jahr für Bauaktennummern.</summary>
public sealed class BauakteNumberSequence
{
    public int Year { get; private set; }
    public int LastNumber { get; private set; }
}

internal sealed class BauakteNumberSequenceConfiguration : IEntityTypeConfiguration<BauakteNumberSequence>
{
    public void Configure(EntityTypeBuilder<BauakteNumberSequence> builder)
    {
        builder.ToTable("bauakte_number_sequences");
        builder.HasKey(sequence => sequence.Year);
        builder.Property(sequence => sequence.Year).ValueGeneratedNever();
    }
}
