// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class ServiceInfoConfiguration : IEntityTypeConfiguration<ServiceInfo>
{
    public void Configure(EntityTypeBuilder<ServiceInfo> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.Name }).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany(s => s.Services)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServerMetricConfiguration : IEntityTypeConfiguration<ServerMetric>
{
    public void Configure(EntityTypeBuilder<ServerMetric> builder)
    {
        builder.HasIndex(e => new { e.ServerId, e.Timestamp });
        builder.HasIndex(e => e.Timestamp);
        builder.HasOne(e => e.Server)
               .WithMany(s => s.Metrics)
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> builder)
    {
        builder.HasIndex(e => e.Name).IsUnique();
        builder.HasIndex(e => e.ProvisioningKey).IsUnique();
        builder.HasOne(e => e.Server)
               .WithMany()
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.NotificationChannel)
               .WithMany()
               .HasForeignKey(e => e.NotificationChannelId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
