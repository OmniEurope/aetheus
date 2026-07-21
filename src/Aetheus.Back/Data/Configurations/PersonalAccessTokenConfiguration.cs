// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class PersonalAccessTokenConfiguration : IEntityTypeConfiguration<PersonalAccessToken>
{
    public void Configure(EntityTypeBuilder<PersonalAccessToken> builder)
    {
        // Stores an HMAC-SHA256 hash (plaintext never persisted), mirroring RegistrationToken.Token /
        // ServerToken.TokenHash / RefreshToken.TokenHash - fixed 64-char width.
        builder.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => e.TokenHash).IsUnique();

        builder.Property(e => e.Name).HasMaxLength(64).IsRequired();
        builder.Property(e => e.TokenPrefix).HasMaxLength(32).IsRequired();

        builder.HasIndex(e => e.UserId);

        builder.HasOne(e => e.User)
               .WithMany()
               .HasForeignKey(e => e.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
