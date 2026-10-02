// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AppMonitoring.Ingest;

/// <summary>
/// Recette R2-013: a refused ingestion key is a fault of the sender (a running application that holds
/// a key that is no longer current nor kept, or that sends none), so it is a warning, but one exporter
/// retries every few seconds and would write one line per request. One warning per key per
/// <see cref="Period"/>: the first refusal is written at once, the following ones are counted and their
/// number is written with the first refusal of the next period. A key is named by the first characters
/// of its hash, which identify it without revealing it.
/// </summary>
public sealed class IngestKeyRejectionLog(TimeProvider time, ILogger<IngestKeyRejectionLog> logger)
{
    internal static readonly TimeSpan Period = TimeSpan.FromHours(1);

    /// <summary>Distinct keys tracked at once; beyond, refusals share the <see cref="OtherKeys"/> entry.</summary>
    internal const int MaxTrackedKeys = 256;

    internal const string MissingKey = "missing";
    internal const string OtherKeys = "other";
    private const int FingerprintLength = 12;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (DateTime Start, int Suppressed)> _windows = new(StringComparer.Ordinal);

    /// <param name="keyHash">The hash of the refused key, or null when the request carried none.</param>
    public void Record(string? keyHash)
    {
        var fingerprint = keyHash is null
            ? MissingKey
            : keyHash[..Math.Min(FingerprintLength, keyHash.Length)];
        var now = time.GetUtcNow().UtcDateTime;
        DateTime? previousReport;
        int count;
        lock (_gate)
        {
            if (!_windows.ContainsKey(fingerprint) && _windows.Count >= MaxTrackedKeys)
            {
                foreach (var expired in _windows.Where(entry => now - entry.Value.Start >= Period).Select(entry => entry.Key).ToList())
                    _windows.Remove(expired);
                if (_windows.Count >= MaxTrackedKeys)
                    fingerprint = OtherKeys;
            }

            if (_windows.TryGetValue(fingerprint, out var window) && now - window.Start < Period)
            {
                _windows[fingerprint] = (window.Start, window.Suppressed + 1);
                return;
            }

            previousReport = window.Suppressed > 0 ? window.Start : null;
            count = window.Suppressed + 1;
            _windows[fingerprint] = (now, 0);
        }

        if (previousReport is { } reportedAt)
            logger.LogWarning(
                "Telemetry ingest refused key {KeyFingerprint} {Count} more time(s) since the report of {ReportedAt:O}; the sender still holds no valid ingest key",
                fingerprint,
                count,
                reportedAt);
        else
            logger.LogWarning(
                "Telemetry ingest refused key {KeyFingerprint}: the sender holds no valid ingest key. Further refusals of this key are summarised once per {PeriodMinutes} minutes",
                fingerprint,
                Period.TotalMinutes);
    }
}
