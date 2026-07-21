// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class ModuleLinkConfiguration : IEntityTypeConfiguration<ModuleLink>
{
    public void Configure(EntityTypeBuilder<ModuleLink> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.SourceType, e.SourceIdentifier, e.TargetType, e.TargetIdentifier }).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany(s => s.ModuleLinks)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
