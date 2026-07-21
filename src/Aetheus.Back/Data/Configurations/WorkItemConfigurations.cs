// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.Property(w => w.Title).HasMaxLength(300).IsRequired();
        builder.Property(w => w.Description).HasMaxLength(4000);
        builder.Property(w => w.Tags).HasMaxLength(1000);
        builder.Property(w => w.ExternalId).HasMaxLength(100);
        builder.Property(w => w.ExternalUrl).HasMaxLength(500);
        builder.HasOne(w => w.Project)
            .WithMany()
            .HasForeignKey(w => w.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(w => w.Assignee)
            .WithMany()
            .HasForeignKey(w => w.AssigneeUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(w => w.Parent)
            .WithMany(w => w.Children)
            .HasForeignKey(w => w.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(w => w.LinkedPipelineRun)
            .WithMany()
            .HasForeignKey(w => w.LinkedPipelineRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
