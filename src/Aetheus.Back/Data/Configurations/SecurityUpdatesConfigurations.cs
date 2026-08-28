// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class SecurityUpdatesStateConfiguration : IEntityTypeConfiguration<SecurityUpdatesState>
{
    public void Configure(EntityTypeBuilder<SecurityUpdatesState> builder)
    {
        builder.HasIndex(e => e.ServerId).IsUnique();
        builder.HasOne(e => e.Server)
               .WithOne(s => s.SecurityUpdatesState)
               .HasForeignKey<SecurityUpdatesState>(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
