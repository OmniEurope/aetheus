// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aetheus.Back.Data.Configurations;

internal sealed class NotificationChannelConfiguration : IEntityTypeConfiguration<NotificationChannel>
{
    public void Configure(EntityTypeBuilder<NotificationChannel> builder)
    {
        builder.HasIndex(c => c.Name).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.ConfigurationJson).HasMaxLength(4000);
    }
}

internal sealed class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public void Configure(EntityTypeBuilder<NotificationRule> builder)
    {
        builder.Property(r => r.EventType).HasMaxLength(100).IsRequired();
        builder.Property(r => r.FilterJson).HasMaxLength(2000);
        builder.HasOne(r => r.Channel)
            .WithMany(c => c.Rules)
            .HasForeignKey(r => r.NotificationChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServiceConnectionConfiguration : IEntityTypeConfiguration<ServiceConnection>
{
    public void Configure(EntityTypeBuilder<ServiceConnection> builder)
    {
        builder.HasIndex(sc => sc.Name).IsUnique();
        builder.Property(sc => sc.Name).HasMaxLength(100).IsRequired();
        builder.Property(sc => sc.Description).HasMaxLength(500);
        builder.Property(sc => sc.EncryptedPayload).HasMaxLength(8000).IsRequired();
        builder.Property(sc => sc.Url).HasMaxLength(500);
        builder.HasOne(sc => sc.Project)
            .WithMany()
            .HasForeignKey(sc => sc.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.Property(w => w.EventType).HasMaxLength(100).IsRequired();
        builder.Property(w => w.TargetUrl).HasMaxLength(500).IsRequired();
        builder.Property(w => w.Secret).HasMaxLength(200);
    }
}
