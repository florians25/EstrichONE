using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Contacts;

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("contacts");
        builder.ConfigureVersioned();
        builder.Property(contact => contact.Id).ValueGeneratedNever();
        builder.Property(contact => contact.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(contact => contact.Name).HasMaxLength(200);
        builder.Property(contact => contact.CustomerNumber).HasMaxLength(50);
        builder.Property(contact => contact.Street).HasMaxLength(200);
        builder.Property(contact => contact.PostalCode).HasMaxLength(10);
        builder.Property(contact => contact.City).HasMaxLength(100);
        builder.Property(contact => contact.Phone).HasMaxLength(50);
        builder.Property(contact => contact.Email).HasMaxLength(320);
        builder.Property(contact => contact.Notes).HasMaxLength(4000);
        builder.HasIndex(contact => contact.Name);
        builder.HasIndex(contact => contact.CustomerNumber).IsUnique().HasFilter("customer_number IS NOT NULL");
        builder.HasMany(contact => contact.Persons).WithOne().HasForeignKey(person => person.ContactId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContactPersonConfiguration : IEntityTypeConfiguration<ContactPerson>
{
    public void Configure(EntityTypeBuilder<ContactPerson> builder)
    {
        builder.ToTable("contact_persons");
        builder.Property(person => person.Id).ValueGeneratedNever();
        builder.Property(person => person.Name).HasMaxLength(200);
        builder.Property(person => person.Function).HasMaxLength(100);
        builder.Property(person => person.Phone).HasMaxLength(50);
        builder.Property(person => person.Email).HasMaxLength(320);
    }
}
