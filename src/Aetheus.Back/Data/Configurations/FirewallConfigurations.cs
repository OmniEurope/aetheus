// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class FirewallStateConfiguration : IEntityTypeConfiguration<FirewallState>
{
    public void Configure(EntityTypeBuilder<FirewallState> builder)
    {
        builder.HasIndex(e => e.ServerId).IsUnique();
        builder.Property(e => e.CollectionDiagnostics).HasMaxLength(1024);
        builder.HasOne(e => e.Server)
               .WithOne(s => s.FirewallState)
               .HasForeignKey<FirewallState>(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
