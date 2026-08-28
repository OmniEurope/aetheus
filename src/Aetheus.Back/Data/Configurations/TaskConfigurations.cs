// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class ServerTaskConfiguration : IEntityTypeConfiguration<ServerTask>
{
    public void Configure(EntityTypeBuilder<ServerTask> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.Status });
        // TaskTimeoutService sweeps (every minute) and the retention purge filter on
        // Status + a date with no ServerId: without these, they seq-scan the whole
        // ever-growing Tasks table.
        builder.HasIndex(e => new { e.Status, e.CreatedAt });
        builder.HasIndex(e => new { e.Status, e.AssignedAt });
        builder.HasIndex(e => new { e.Status, e.StartedAt });
        builder.HasIndex(e => new { e.IsDeferredCleanup, e.Status });
        builder.HasIndex(e => e.PipelineRunId);
        builder.HasIndex(e => new { e.PipelineRunId, e.IsDeferredCleanup })
            .IsUnique()
            .HasFilter("\"IsDeferredCleanup\" = TRUE AND \"PipelineRunId\" IS NOT NULL");
        builder.Property(e => e.AssignedAgentSessionId).HasMaxLength(64);
        builder.Property(e => e.AssignedAgentVersion).HasMaxLength(64);
        builder.Property(e => e.AssignedScannerManifestSha256).HasMaxLength(64);
        builder.Property(e => e.ContainerToolchain).HasMaxLength(64);
        builder.Property(e => e.ContainerShell).HasMaxLength(16);
        builder.Property(e => e.FailureCode).HasMaxLength(64);
        builder.Property(e => e.FailureReason).HasMaxLength(2048);
        builder.HasOne(e => e.Server)
               .WithMany(s => s.Tasks)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.PipelineRun)
               .WithMany(r => r.Tasks)
               .HasForeignKey(e => e.PipelineRunId)
               .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.PipelineStepRun)
               .WithMany()
               .HasForeignKey(e => e.PipelineStepRunId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TaskLogConfiguration : IEntityTypeConfiguration<TaskLog>
{
    public void Configure(EntityTypeBuilder<TaskLog> builder)
    {
        builder.HasIndex(e => new { e.TaskId, e.Timestamp });
        builder.HasIndex(e => e.Timestamp);
        builder.HasOne(e => e.Task)
               .WithMany(t => t.Logs)
               .HasForeignKey(e => e.TaskId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
