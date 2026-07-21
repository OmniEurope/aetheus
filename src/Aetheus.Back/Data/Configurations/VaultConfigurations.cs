// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class VaultConfiguration : IEntityTypeConfiguration<Vault>
{
    public void Configure(EntityTypeBuilder<Vault> builder)
    {
        // Per-owner name uniqueness via partial unique indexes (exactly one owner FK set).
        builder.HasIndex(e => new { e.Name, e.ProjectId }).IsUnique().HasFilter("\"ProjectId\" IS NOT NULL");
        builder.HasIndex(e => new { e.Name, e.EnvironmentId }).IsUnique().HasFilter("\"EnvironmentId\" IS NOT NULL");
        builder.HasIndex(e => new { e.Name, e.ProjectServerId }).IsUnique().HasFilter("\"ProjectServerId\" IS NOT NULL");
        builder.HasOne(e => e.Project)
               .WithMany()
               .HasForeignKey(e => e.ProjectId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Environment)
               .WithMany(env => env.Vaults)
               .HasForeignKey(e => e.EnvironmentId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ProjectServer)
               .WithMany(ps => ps.Vaults)
               .HasForeignKey(e => e.ProjectServerId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VaultSecretConfiguration : IEntityTypeConfiguration<VaultSecret>
{
    public void Configure(EntityTypeBuilder<VaultSecret> builder)
    {
        builder.HasIndex(e => new { e.VaultId, e.Key }).IsUnique();
        builder.HasOne(e => e.Vault)
               .WithMany(v => v.Secrets)
               .HasForeignKey(e => e.VaultId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VaultSecretVersionConfiguration : IEntityTypeConfiguration<VaultSecretVersion>
{
    public void Configure(EntityTypeBuilder<VaultSecretVersion> builder)
    {
        builder.HasIndex(e => e.VaultSecretId);
        builder.HasOne(e => e.VaultSecret)
               .WithMany(s => s.Versions)
               .HasForeignKey(e => e.VaultSecretId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
