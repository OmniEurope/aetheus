// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class PipelineResourceLockConfiguration : IEntityTypeConfiguration<PipelineResourceLock>
{
    public void Configure(EntityTypeBuilder<PipelineResourceLock> builder)
    {
        builder.HasKey(l => l.Key);
        builder.HasIndex(l => l.PipelineRunId);
        // A deleted run takes its locks with it (retention, project deletion).
        builder.HasOne(l => l.PipelineRun)
               .WithMany()
               .HasForeignKey(l => l.PipelineRunId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
