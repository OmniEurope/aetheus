// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class PluginRegistrationConfiguration : IEntityTypeConfiguration<PluginRegistration>
{
    public void Configure(EntityTypeBuilder<PluginRegistration> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.Version).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Author).HasMaxLength(200);
        builder.Property(p => p.EntryPoint).HasMaxLength(500);
        builder.Property(p => p.ConfigurationJson).HasMaxLength(4000);
        builder.HasIndex(p => new { p.Name, p.Version }).IsUnique();
        builder.HasIndex(p => p.OrganizationId);
        builder.HasOne(p => p.Organization)
               .WithMany()
               .HasForeignKey(p => p.OrganizationId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
