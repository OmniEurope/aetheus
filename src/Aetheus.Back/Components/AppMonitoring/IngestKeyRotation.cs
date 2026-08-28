// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

internal static class IngestKeyRotation
{
    public static (string? PreviousHash, string NewHash, DateTime CreatedAt) Apply(
        MonitoredApp app,
        string plaintext,
        IngestKeyHasher hasher,
        IConfiguration configuration,
        TimeProvider timeProvider)
    {
        var previousHash = app.IngestKeyHash;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newHash = hasher.Hash(plaintext);
        var overlapDays = Math.Clamp(
            configuration.GetValue(
                "AppMonitoring:IngestKeyOverlapDays",
                AppMonitoringDefaults.DefaultIngestKeyOverlapDays),
            0,
            AppMonitoringDefaults.MaximumIngestKeyOverlapDays);
        app.PreviousIngestKeyHash = previousHash;
        app.PreviousIngestKeyValidUntil = previousHash is null ? null : now.AddDays(overlapDays);
        app.IngestKeyHash = newHash;
        app.IngestKeyCreatedAt = now;
        app.IngestKeyExpiresAt = now.AddDays(AppMonitoringDefaults.MaximumIngestKeyLifetimeDays);
        app.IngestKeyVersion++;
        return (previousHash, newHash, now);
    }
}
