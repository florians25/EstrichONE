using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
        builder.Property(contact => contact.Phones).HasColumnType("jsonb").HasConversion(Json<List<ContactPhone>>.Converter(() => []), Json<List<ContactPhone>>.Comparer());
        builder.Property(contact => contact.Emails).HasColumnType("jsonb").HasConversion(Json<List<ContactEmail>>.Converter(() => []), Json<List<ContactEmail>>.Comparer());
        builder.Property(contact => contact.BillingAddress).HasColumnType("jsonb").HasConversion(Json<ContactAddress?>.Converter(() => null), Json<ContactAddress?>.Comparer());
        builder.Property(contact => contact.DeliveryAddress).HasColumnType("jsonb").HasConversion(Json<ContactAddress?>.Converter(() => null), Json<ContactAddress?>.Comparer());
        builder.Property(contact => contact.Salutation).HasMaxLength(30);
        builder.Property(contact => contact.Title).HasMaxLength(50);
        builder.Property(contact => contact.VatId).HasMaxLength(30);
        builder.Property(contact => contact.TaxNumber).HasMaxLength(30);
        builder.Property(contact => contact.PreferredChannel).HasConversion<string>().HasMaxLength(20);
        builder.Property(contact => contact.Website).HasMaxLength(200);
        builder.HasIndex(contact => contact.Name);
        builder.HasIndex(contact => contact.CustomerNumber).IsUnique().HasFilter("customer_number IS NOT NULL");
        builder.HasMany(contact => contact.Persons).WithOne().HasForeignKey(person => person.ContactId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>JSON-Spalten (jsonb) mit Aufzählungen als Text, z. B. {"kind":"mobile","number":"0171 …"}.</summary>
internal static class Json<T>
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ValueConverter<T, string> Converter(Func<T> empty) => new(
        value => JsonSerializer.Serialize(value, Options),
        json => string.IsNullOrEmpty(json) ? empty() : JsonSerializer.Deserialize<T>(json, Options) ?? empty());

    public static ValueComparer<T> Comparer() => new(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
        v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!);
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
