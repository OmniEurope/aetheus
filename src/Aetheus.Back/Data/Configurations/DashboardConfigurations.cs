// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
{
    public void Configure(EntityTypeBuilder<Dashboard> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(d => new { d.UserId, d.Name }).IsUnique();
        builder.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DashboardWidgetConfiguration : IEntityTypeConfiguration<DashboardWidget>
{
    public void Configure(EntityTypeBuilder<DashboardWidget> builder)
    {
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Title).HasMaxLength(100).IsRequired();
        builder.Property(w => w.ConfigurationJson).HasMaxLength(2000);
        builder.HasOne(w => w.Dashboard).WithMany(d => d.Widgets).HasForeignKey(w => w.DashboardId).OnDelete(DeleteBehavior.Cascade);
    }
}
