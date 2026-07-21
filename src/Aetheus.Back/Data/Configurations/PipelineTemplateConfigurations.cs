// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class PipelineTemplateConfiguration : IEntityTypeConfiguration<PipelineTemplate>
{
    public void Configure(EntityTypeBuilder<PipelineTemplate> builder)
    {
        builder.Property(template => template.Name).HasMaxLength(100).IsRequired();
        builder.Property(template => template.Description).HasMaxLength(500);
        builder.Property(template => template.Category).HasMaxLength(50).IsRequired();
        builder.Property(template => template.LatestVersion).IsConcurrencyToken();
        builder.HasIndex(template => new { template.OrganizationId, template.Name }).IsUnique();
        builder.HasOne(template => template.Organization)
            .WithMany()
            .HasForeignKey(template => template.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PipelineTemplateVersionConfiguration : IEntityTypeConfiguration<PipelineTemplateVersion>
{
    public void Configure(EntityTypeBuilder<PipelineTemplateVersion> builder)
    {
        builder.Property(version => version.YamlContent).HasMaxLength(50_000).IsRequired();
        builder.Property(version => version.ChangelogEntry).HasMaxLength(500).IsRequired();
        builder.Property(version => version.CreatedByUsername).HasMaxLength(256).IsRequired();
        builder.HasIndex(version => new { version.TemplateId, version.Version }).IsUnique();
        builder.HasOne(version => version.Template)
            .WithMany(template => template.Versions)
            .HasForeignKey(version => version.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
