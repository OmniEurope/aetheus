// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.PortRegistry;

/// <summary>
/// Writes what a scan SAW, as opposed to what somebody CLAIMED. The two are deliberately separate
/// responsibilities: a claim is a decision the registry defends, an observation is evidence it merely
/// records, and no discrepancy between them is ever turned into an automatic correction.
///
/// Everything here is keyed by (port, protocol). The two stacks bind independently, so a host
/// listening on 53/tcp and 53/udp occupies two different ports and collapsing them would hide one
/// behind the other.
/// </summary>
internal sealed class PortObservationWriter(
    IPortRegistryRepository repo,
    TimeProvider timeProvider,
    ILogger logger)
{
    /// <summary>Every observed row shares one owner key: an observation names no project, and pretending
    /// otherwise would let a sighting win a conflict against a real declaration.</summary>
    internal const string ObservedOwnerKey = "observed";

    /// <summary>Shown when neither a container name nor a process name could be read on the host.</summary>
    internal const string ObservedFallbackLabel = "host process";

    /// <summary>
    /// Anything that is not recognisably UDP is read as TCP: that is what every Aetheus deployment
    /// binds, so an unreadable protocol must fall on the side that is actually defended rather than
    /// create a phantom UDP row nothing would ever check against.
    /// </summary>
    internal static string NormalizeProtocol(string? protocol) =>
        string.Equals(protocol?.Trim(), PortRegistryLimits.UdpProtocol, StringComparison.OrdinalIgnoreCase)
            ? PortRegistryLimits.UdpProtocol
            : PortRegistryLimits.TcpProtocol;

    /// <summary>
    /// Replaces the whole <c>Observed</c> source of one server. A shorter list really means "these
    /// ports stopped listening", never "the report was truncated", which is why the caller must send
    /// the complete scan.
    /// </summary>
    public async Task ReplaceAsync(
        int serverId, IReadOnlyCollection<ObservedPortDto> ports, DateTime observedAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ports);
        if (!await repo.SetPortsObservedAtAsync(serverId, observedAt, ct).ConfigureAwait(false))
        {
            // The server was deleted between the scan and its report. Writing observations for it would
            // resurrect rows the cascade just removed.
            logger.LogDebug("Dropped a port observation for unknown server {ServerId}.", serverId);
            return;
        }

        var seen = new Dictionary<(int Port, string Protocol), ObservedPortDto>();
        foreach (var observed in ports)
        {
            if (observed is null || observed.Port is <= 0 or > 65535) continue;
            if (seen.Count >= PortRegistryLimits.MaxObservedPorts) break;
            // The same port listening on several interfaces is one occupied port.
            seen.TryAdd((observed.Port, NormalizeProtocol(observed.Protocol)), observed);
        }

        var existing = await repo.GetTrackedObservedReservationsAsync(serverId, ct).ConfigureAwait(false);
        foreach (var stale in existing.Where(reservation =>
                     !seen.ContainsKey((reservation.Port, NormalizeProtocol(reservation.Protocol)))))
        {
            repo.RemoveReservation(stale);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var ((port, protocol), observed) in seen)
        {
            var label = Truncate(observed.Holder, PortRegistryLimits.MaxOwnerLabelLength);
            if (label.Length == 0) label = ObservedFallbackLabel;

            var row = existing.Find(reservation =>
                reservation.Port == port && NormalizeProtocol(reservation.Protocol) == protocol);
            if (row is null)
            {
                repo.TrackReservation(new ServerPortReservation
                {
                    ServerId = serverId,
                    Port = port,
                    Protocol = protocol,
                    OwnerKey = ObservedOwnerKey,
                    OwnerLabel = label,
                    Source = PortReservationSource.Observed,
                    DeclaredAt = now,
                    UpdatedAt = now,
                    ObservedAt = observedAt
                });
                continue;
            }

            row.OwnerLabel = label;
            row.UpdatedAt = now;
            row.ObservedAt = observedAt;
        }

        // A declared or manual claim confirmed by this scan gets its observation stamp, which is what
        // the UI reads to say "declared AND listening" instead of "declared, never seen". TCP only:
        // everything Aetheus declares is TCP, so a UDP sighting confirms nothing about a claim.
        await StampClaimsAsync(
            serverId,
            [.. seen.Keys
                .Where(key => key.Protocol == PortRegistryLimits.TcpProtocol)
                .Select(key => key.Port)],
            observedAt,
            ct).ConfigureAwait(false);

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks the declared and manual claims this scan confirmed. A claim the scan did NOT see keeps its
    /// previous stamp rather than being cleared: the field means "last seen listening", and the UI reads
    /// "listening now" as a stamp equal to the server's last scan, so stale evidence is never lost and
    /// never mistaken for a live listener either.
    /// </summary>
    private async Task StampClaimsAsync(
        int serverId, IReadOnlyCollection<int> observedPorts, DateTime observedAt, CancellationToken ct)
    {
        if (observedPorts.Count == 0) return;

        foreach (var source in new[] { PortReservationSource.Declared, PortReservationSource.Manual })
        {
            var tracked = await repo
                .GetTrackedReservationsForPortsAsync(serverId, observedPorts, source, ct)
                .ConfigureAwait(false);
            foreach (var row in tracked)
                row.ObservedAt = observedAt;
        }
    }

    private static string Truncate(string value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
