// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

/// <summary>What a rotation changed: <paramref name="AffectedHashes"/> resolve differently now and must
/// leave the ingest key cache, whether they were demoted, dropped or newly issued.</summary>
internal readonly record struct IngestKeyRotationResult(
    string? PreviousHash,
    DateTime CreatedAt,
    IReadOnlyList<string> AffectedHashes);

internal static class IngestKeyRotation
{
    /// <summary>
    /// A rotation asked by an operator (generate a new key): the replaced key stays valid for the
    /// overlap window, and no older key survives it.
    /// </summary>
    public static IngestKeyRotationResult Apply(
        MonitoredApp app,
        string plaintext,
        IngestKeyHasher hasher,
        IConfiguration configuration,
        TimeProvider timeProvider) =>
        Rotate(app, plaintext, hasher, configuration, timeProvider, keepSecondPrevious: false);

    /// <summary>
    /// Recette R2-013: the rotation a deployment run performs. Blue-green keeps two colours alive and
    /// each holds the key of the run that started it, so when a new run rotates, the active colour holds
    /// the previous key and the idle colour the one before, until the run replaces that colour. Keeping
    /// a single previous key refused the idle colour's telemetry (401) from the rotation until its
    /// restart. The key before the previous one is kept with the deadline it received when it stopped
    /// being current: nothing lives longer than the overlap window it was already given.
    /// </summary>
    public static IngestKeyRotationResult ApplyForDeploy(
        MonitoredApp app,
        string plaintext,
        IngestKeyHasher hasher,
        IConfiguration configuration,
        TimeProvider timeProvider) =>
        Rotate(app, plaintext, hasher, configuration, timeProvider, keepSecondPrevious: true);

    private static IngestKeyRotationResult Rotate(
        MonitoredApp app,
        string plaintext,
        IngestKeyHasher hasher,
        IConfiguration configuration,
        TimeProvider timeProvider,
        bool keepSecondPrevious)
    {
        var affected = new List<string>(4);
        AddIfPresent(affected, app.SecondPreviousIngestKeyHash);
        AddIfPresent(affected, app.PreviousIngestKeyHash);

        var previousHash = app.IngestKeyHash;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newHash = hasher.Hash(plaintext);
        var overlapDays = Math.Clamp(
            configuration.GetValue(
                "AppMonitoring:IngestKeyOverlapDays",
                AppMonitoringDefaults.DefaultIngestKeyOverlapDays),
            0,
            AppMonitoringDefaults.MaximumIngestKeyOverlapDays);
        if (keepSecondPrevious)
        {
            app.SecondPreviousIngestKeyHash = app.PreviousIngestKeyHash;
            app.SecondPreviousIngestKeyValidUntil = app.PreviousIngestKeyHash is null
                ? null
                : app.PreviousIngestKeyValidUntil;
        }
        else
        {
            app.SecondPreviousIngestKeyHash = null;
            app.SecondPreviousIngestKeyValidUntil = null;
        }
        app.PreviousIngestKeyHash = previousHash;
        app.PreviousIngestKeyValidUntil = previousHash is null ? null : now.AddDays(overlapDays);
        app.IngestKeyHash = newHash;
        app.IngestKeyCreatedAt = now;
        app.IngestKeyExpiresAt = now.AddDays(AppMonitoringDefaults.MaximumIngestKeyLifetimeDays);
        app.IngestKeyVersion++;

        AddIfPresent(affected, previousHash);
        AddIfPresent(affected, newHash);
        return new IngestKeyRotationResult(previousHash, now, affected);
    }

    private static void AddIfPresent(List<string> hashes, string? hash)
    {
        if (hash is not null && !hashes.Contains(hash, StringComparer.Ordinal))
            hashes.Add(hash);
    }
}
