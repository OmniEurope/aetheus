// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class TeamspeakStateConfiguration : IEntityTypeConfiguration<TeamspeakState>
{
    public void Configure(EntityTypeBuilder<TeamspeakState> builder)
    {
        builder.HasIndex(e => e.ServerId).IsUnique();
        builder.HasOne(e => e.Server)
               .WithOne(s => s.TeamspeakState)
               .HasForeignKey<TeamspeakState>(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamspeakChannelConfiguration : IEntityTypeConfiguration<TeamspeakChannel>
{
    public void Configure(EntityTypeBuilder<TeamspeakChannel> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.ChannelId }).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany(s => s.TeamspeakChannels)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamspeakClientConfiguration : IEntityTypeConfiguration<TeamspeakClient>
{
    public void Configure(EntityTypeBuilder<TeamspeakClient> builder)
    {
        builder.HasIndex(entity => new { entity.ServerId, entity.ClientId }).IsUnique();
        builder.HasIndex(entity => new { entity.ServerId, entity.Nickname });
        builder.Property(entity => entity.UniqueId).HasMaxLength(128);
        builder.Property(entity => entity.Nickname).HasMaxLength(128);
        builder.Property(entity => entity.ChannelName).HasMaxLength(128);
        builder.Property(entity => entity.Platform).HasMaxLength(64);
        builder.Property(entity => entity.Version).HasMaxLength(128);
        builder.HasOne(entity => entity.Server)
            .WithMany(server => server.TeamspeakClients)
            .HasForeignKey(entity => entity.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamspeakBanConfiguration : IEntityTypeConfiguration<TeamspeakBan>
{
    public void Configure(EntityTypeBuilder<TeamspeakBan> builder)
    {
        builder.HasIndex(entity => new { entity.ServerId, entity.BanId }).IsUnique();
        builder.HasIndex(entity => new { entity.ServerId, entity.Nickname });
        builder.Property(entity => entity.Ip).HasMaxLength(64);
        builder.Property(entity => entity.UniqueId).HasMaxLength(128);
        builder.Property(entity => entity.Nickname).HasMaxLength(128);
        builder.Property(entity => entity.Reason).HasMaxLength(512);
        builder.HasOne(entity => entity.Server)
            .WithMany(server => server.TeamspeakBans)
            .HasForeignKey(entity => entity.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
