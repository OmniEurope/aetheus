// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class BackupPolicyConfiguration : IEntityTypeConfiguration<BackupPolicy>
{
    public void Configure(EntityTypeBuilder<BackupPolicy> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(120).IsRequired();
        builder.Property(e => e.ScheduleCron).HasMaxLength(120).IsRequired();
        builder.Property(e => e.DbHost).HasMaxLength(255);
        builder.Property(e => e.DbName).HasMaxLength(120);
        builder.Property(e => e.DbUser).HasMaxLength(120);
        builder.HasIndex(e => e.ProjectId);
        builder.HasIndex(e => e.ServerId);

        builder.HasOne(e => e.Project)
               .WithMany()
               .HasForeignKey(e => e.ProjectId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Server)
               .WithMany()
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BackupRunConfiguration : IEntityTypeConfiguration<BackupRun>
{
    public void Configure(EntityTypeBuilder<BackupRun> builder)
    {
        builder.HasIndex(e => e.BackupPolicyId);
        builder.HasIndex(e => new { e.BackupPolicyId, e.StartedAt });
        builder.HasOne(e => e.BackupPolicy)
               .WithMany(p => p.Runs)
               .HasForeignKey(e => e.BackupPolicyId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
