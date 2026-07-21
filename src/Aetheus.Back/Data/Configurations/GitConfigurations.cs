// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class GitConnectionConfiguration : IEntityTypeConfiguration<GitConnection>
{
    public void Configure(EntityTypeBuilder<GitConnection> builder)
    {
        builder.HasIndex(g => new { g.ProjectId, g.ProviderType, g.OwnerOrGroup, g.RepositoryName }).IsUnique();
        builder.Property(g => g.OwnerOrGroup).HasMaxLength(200).IsRequired();
        builder.Property(g => g.RepositoryName).HasMaxLength(200).IsRequired();
        builder.Property(g => g.BaseUrl).HasMaxLength(500);
        builder.Property(g => g.MirrorPath).HasMaxLength(1000);
        builder.Property(g => g.LastFetchError).HasMaxLength(2000);
        builder.HasOne(g => g.Project)
            .WithMany()
            .HasForeignKey(g => g.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(g => g.ServiceConnection)
            .WithMany()
            .HasForeignKey(g => g.ServiceConnectionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PullRequestConfiguration : IEntityTypeConfiguration<PullRequest>
{
    public void Configure(EntityTypeBuilder<PullRequest> builder)
    {
        builder.HasIndex(p => new { p.GitConnectionId, p.ExternalId }).IsUnique();
        builder.Property(p => p.Title).HasMaxLength(500).IsRequired();
        builder.Property(p => p.SourceBranch).HasMaxLength(300).IsRequired();
        builder.Property(p => p.TargetBranch).HasMaxLength(300).IsRequired();
        builder.Property(p => p.AuthorLogin).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ExternalUrl).HasMaxLength(500);
        builder.Property(p => p.HeadCommitSha).HasMaxLength(40);
        builder.Property(p => p.MergeCommitSha).HasMaxLength(40);
        builder.HasOne(p => p.GitConnection)
            .WithMany(g => g.PullRequests)
            .HasForeignKey(p => p.GitConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.LinkedPipelineRun)
            .WithMany()
            .HasForeignKey(p => p.LinkedPipelineRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class BranchPolicyConfiguration : IEntityTypeConfiguration<BranchPolicy>
{
    public void Configure(EntityTypeBuilder<BranchPolicy> builder)
    {
        builder.Property(b => b.BranchPattern).HasMaxLength(300).IsRequired();
        builder.Property(b => b.ConfigurationJson).HasMaxLength(2000);
        builder.HasOne(b => b.GitConnection)
            .WithMany(g => g.BranchPolicies)
            .HasForeignKey(b => b.GitConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
