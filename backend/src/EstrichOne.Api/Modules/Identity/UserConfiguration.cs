using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.ConfigureVersioned();
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.DisplayName).HasMaxLength(User.MaxNameLength);
        builder.Property(user => user.UserName).HasMaxLength(User.MaxUserNameLength);
        builder.Property(user => user.NormalizedUserName).HasMaxLength(User.MaxUserNameLength);
        builder.Property(user => user.Email).HasMaxLength(User.MaxEmailLength);
        builder.Property(user => user.NormalizedEmail).HasMaxLength(User.MaxEmailLength);
        builder.Property(user => user.Phone).HasMaxLength(50);
        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
        builder.HasIndex(user => user.NormalizedEmail).IsUnique().HasFilter("normalized_email IS NOT NULL");
    }
}
