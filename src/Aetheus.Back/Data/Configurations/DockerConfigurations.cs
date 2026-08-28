// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class DockerContainerConfiguration : IEntityTypeConfiguration<DockerContainer>
{
    public void Configure(EntityTypeBuilder<DockerContainer> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.ContainerId }).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany(s => s.DockerContainers)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DockerImageConfiguration : IEntityTypeConfiguration<DockerImage>
{
    public void Configure(EntityTypeBuilder<DockerImage> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.ImageId }).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany(s => s.DockerImages)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DockerComposeStackConfiguration : IEntityTypeConfiguration<DockerComposeStack>
{
    public void Configure(EntityTypeBuilder<DockerComposeStack> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.Name }).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany(s => s.DockerComposeStacks)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DockerNetworkConfiguration : IEntityTypeConfiguration<DockerNetwork>
{
    public void Configure(EntityTypeBuilder<DockerNetwork> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.NetworkId }).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany(s => s.DockerNetworks)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DockerVolumeConfiguration : IEntityTypeConfiguration<DockerVolume>
{
    public void Configure(EntityTypeBuilder<DockerVolume> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.Name }).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany(s => s.DockerVolumes)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
