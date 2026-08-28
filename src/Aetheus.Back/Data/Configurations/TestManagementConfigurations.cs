// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class TestSuiteConfiguration : IEntityTypeConfiguration<TestSuite>
{
    public void Configure(EntityTypeBuilder<TestSuite> builder)
    {
        builder.HasIndex(s => new { s.ProjectId, s.Name }).IsUnique();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.HasOne(s => s.Project)
            .WithMany()
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Pipeline)
            .WithMany()
            .HasForeignKey(s => s.PipelineId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TestCaseConfiguration : IEntityTypeConfiguration<TestCase>
{
    public void Configure(EntityTypeBuilder<TestCase> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(500).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(2000);
        builder.Property(c => c.AutomatedTestClass).HasMaxLength(500);
        builder.Property(c => c.AutomatedTestMethod).HasMaxLength(500);
        builder.HasOne(c => c.TestSuite)
            .WithMany(s => s.TestCases)
            .HasForeignKey(c => c.TestSuiteId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.LastPipelineRun)
            .WithMany()
            .HasForeignKey(c => c.LastPipelineRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
