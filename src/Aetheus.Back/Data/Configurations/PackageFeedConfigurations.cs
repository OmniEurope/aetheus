// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class PackageFeedConfiguration : IEntityTypeConfiguration<PackageFeed>
{
    public void Configure(EntityTypeBuilder<PackageFeed> builder)
    {
        builder.HasIndex(f => f.Name).IsUnique();
        builder.Property(f => f.Name).HasMaxLength(100).IsRequired();
        builder.Property(f => f.Description).HasMaxLength(500);
        builder.Property(f => f.UpstreamUrl).HasMaxLength(500).IsRequired();
        builder.HasOne(f => f.Project)
            .WithMany()
            .HasForeignKey(f => f.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(f => f.ServiceConnection)
            .WithMany()
            .HasForeignKey(f => f.ServiceConnectionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PackageEntryConfiguration : IEntityTypeConfiguration<PackageEntry>
{
    public void Configure(EntityTypeBuilder<PackageEntry> builder)
    {
        builder.HasIndex(p => new { p.PackageFeedId, p.Name }).IsUnique();
        builder.Property(p => p.Name).HasMaxLength(300).IsRequired();
        builder.Property(p => p.LatestVersion).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.HasOne(p => p.PackageFeed)
            .WithMany(f => f.Packages)
            .HasForeignKey(p => p.PackageFeedId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
