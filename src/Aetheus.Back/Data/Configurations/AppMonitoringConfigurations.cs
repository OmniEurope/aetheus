// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Environment = Aetheus.Back.Data.Entities.Environment;

namespace Aetheus.Back.Data.Configurations;

internal sealed class MonitoredAppConfiguration : IEntityTypeConfiguration<MonitoredApp>
{
    public void Configure(EntityTypeBuilder<MonitoredApp> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(150).IsRequired();
        builder.Property(e => e.ProbeUrl).HasMaxLength(2048);
        builder.Property(e => e.IngestKeyHash).HasMaxLength(128);
        builder.HasIndex(e => e.IngestKeyHash).IsUnique().HasFilter("\"IngestKeyHash\" IS NOT NULL");

        builder.HasIndex(e => e.ProjectId);
        builder.HasIndex(e => e.ServerId);
        builder.HasIndex(e => e.CurrentStatus);

        builder.HasOne(e => e.Project)
               .WithMany()
               .HasForeignKey(e => e.ProjectId)
               .OnDelete(DeleteBehavior.Restrict);

        // Optional references: the app survives (and falls back to backend-probed / informative) when
        // the environment or hosting server is removed.
        builder.HasOne(e => e.Environment)
               .WithMany()
               .HasForeignKey(e => e.EnvironmentId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Server)
               .WithMany()
               .HasForeignKey(e => e.ServerId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AppHealthSampleConfiguration : IEntityTypeConfiguration<AppHealthSample>
{
    public void Configure(EntityTypeBuilder<AppHealthSample> builder)
    {
        builder.Property(e => e.Error).HasMaxLength(500);

        builder.HasIndex(e => new { e.MonitoredAppId, e.Timestamp });
        builder.HasIndex(e => e.Timestamp);

        builder.HasOne(e => e.MonitoredApp)
               .WithMany()
               .HasForeignKey(e => e.MonitoredAppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AppHealthHourlyConfiguration : IEntityTypeConfiguration<AppHealthHourly>
{
    public void Configure(EntityTypeBuilder<AppHealthHourly> builder)
    {
        builder.HasIndex(e => new { e.MonitoredAppId, e.HourUtc }).IsUnique();

        builder.HasOne(e => e.MonitoredApp)
               .WithMany()
               .HasForeignKey(e => e.MonitoredAppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AppMetricSampleConfiguration : IEntityTypeConfiguration<AppMetricSample>
{
    public void Configure(EntityTypeBuilder<AppMetricSample> builder)
    {
        builder.Property(e => e.MetricName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(32);
        builder.Property(e => e.AttributesJson).HasMaxLength(1024);

        builder.HasIndex(e => new { e.MonitoredAppId, e.MetricName, e.Timestamp });
        builder.HasIndex(e => e.Timestamp);

        builder.HasOne(e => e.MonitoredApp)
               .WithMany()
               .HasForeignKey(e => e.MonitoredAppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AppMetricHourlyConfiguration : IEntityTypeConfiguration<AppMetricHourly>
{
    public void Configure(EntityTypeBuilder<AppMetricHourly> builder)
    {
        builder.Property(e => e.MetricName).HasMaxLength(200).IsRequired();
        builder.HasIndex(e => new { e.MonitoredAppId, e.MetricName, e.HourUtc }).IsUnique();

        builder.HasOne(e => e.MonitoredApp)
               .WithMany()
               .HasForeignKey(e => e.MonitoredAppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AppMetricThresholdConfiguration : IEntityTypeConfiguration<AppMetricThreshold>
{
    public void Configure(EntityTypeBuilder<AppMetricThreshold> builder)
    {
        builder.Property(e => e.MetricName).HasMaxLength(200).IsRequired();
        builder.HasIndex(e => new { e.MonitoredAppId, e.MetricName });

        builder.HasOne(e => e.MonitoredApp)
               .WithMany()
               .HasForeignKey(e => e.MonitoredAppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AppLogEntryConfiguration : IEntityTypeConfiguration<AppLogEntry>
{
    public void Configure(EntityTypeBuilder<AppLogEntry> builder)
    {
        builder.Property(e => e.SeverityText).HasMaxLength(32);
        builder.Property(e => e.Body).HasMaxLength(4000).IsRequired();
        builder.Property(e => e.AttributesJson).HasMaxLength(1024);

        builder.HasIndex(e => new { e.MonitoredAppId, e.Timestamp });
        builder.HasIndex(e => e.Timestamp);

        builder.HasOne(e => e.MonitoredApp)
               .WithMany()
               .HasForeignKey(e => e.MonitoredAppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AppErrorEventConfiguration : IEntityTypeConfiguration<AppErrorEvent>
{
    public void Configure(EntityTypeBuilder<AppErrorEvent> builder)
    {
        builder.Property(e => e.Fingerprint).HasMaxLength(128).IsRequired();
        builder.Property(e => e.ExceptionType).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Message).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.TopFrame).HasMaxLength(512);

        builder.HasIndex(e => new { e.MonitoredAppId, e.Fingerprint }).IsUnique();
        builder.HasIndex(e => new { e.MonitoredAppId, e.LastSeenAt });

        builder.HasOne(e => e.MonitoredApp)
               .WithMany()
               .HasForeignKey(e => e.MonitoredAppId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
