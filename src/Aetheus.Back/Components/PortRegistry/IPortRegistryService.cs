// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.PortRegistry;

/// <summary>
/// The registry of who holds which port on which server.
///
/// It exists because nothing in Aetheus could answer that question: every project picked its
/// PORT_FRONT/PORT_BACK blind, and the collision only surfaced as
/// <c>Bind for 0.0.0.0:10031 failed: port is already allocated</c> at <c>docker up</c>, naming no
/// holder and leaving the operator with no way to find one without an SSH session.
/// </summary>
public interface IPortRegistryService
{
    Task<List<PortReservationDto>> GetServerReservationsAsync(int serverId, CancellationToken ct = default);

    /// <summary>One verdict per requested port. Never throws on an unknown port, only on an unknown
    /// server. <paramref name="contextProjectId"/> is the project the answer is relative to (PLAN-003
    /// lot 24 / D14): a port that project declared for itself reads as its own, not as taken.</summary>
    Task<PortCheckResultDto> CheckPortsAsync(
        int serverId, IReadOnlyCollection<int> ports, int? contextProjectId = null, CancellationToken ct = default);

    /// <summary>
    /// The ports among <paramref name="ports"/> already claimed on that server by an owner OTHER than
    /// <paramref name="ownerKey"/>. Empty for a redeployment, which re-takes its own ports.
    /// </summary>
    Task<List<PortConflictDto>> FindConflictsAsync(
        int serverId, IReadOnlyCollection<int> ports, string ownerKey, CancellationToken ct = default);

    /// <summary>
    /// Records what a deployment is taking. Idempotent per owner: re-declaring the same set touches
    /// timestamps only, and a declared port the owner no longer asks for is released, so the registry
    /// follows a project that changes its ports instead of accumulating dead claims.
    /// Never steals a port held by another owner - <see cref="FindConflictsAsync"/> is the gate.
    /// </summary>
    Task DeclareAsync(
        int serverId, IReadOnlyCollection<int> ports, string ownerKey, string ownerLabel,
        int? projectId, CancellationToken ct = default);

    Task<PortReservationDto> AddManualReservationAsync(
        int serverId, CreatePortReservationRequest request, CancellationToken ct = default);

    Task<bool> ReleaseAsync(int serverId, int reservationId, CancellationToken ct = default);

    /// <summary>
    /// Replaces the whole <c>Observed</c> source of one server with what a scan just saw, and stamps
    /// the server's last-scan time. Touches no <c>Declared</c> or <c>Manual</c> row: an observation is
    /// evidence, never a decision, so a port declared but not listening keeps its claim and is merely
    /// reported as unobserved. Ports beyond
    /// <see cref="Aetheus.Shared.Components.PortRegistry.PortRegistryLimits.MaxObservedPorts"/> are dropped rather than written.
    /// </summary>
    Task ReplaceObservedAsync(
        int serverId, IReadOnlyCollection<ObservedPortDto> ports, DateTime observedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Releases one port on one server, but only when <paramref name="ownerKey"/> is the holder.
    /// Returns false when the port is free or held by somebody else - releasing another holder's port
    /// is a Server/Write action taken deliberately from the registry page, never a side effect.
    /// </summary>
    Task<bool> ReleaseOwnedAsync(
        int serverId, int port, string ownerKey, CancellationToken ct = default);

    /// <summary>
    /// Releases every port a project holds, on every server, when that project is deleted. Without it
    /// the rows survive the project - their foreign key is set to null, not removed - and the ports
    /// stay blocked forever by an owner that no longer exists. Returns how many were released.
    /// </summary>
    Task<int> ReleaseProjectPortsAsync(int projectId, CancellationToken ct = default);

    /// <summary>
    /// Makes the registry state one owner's single port (PLAN-005 lot 6). Used by the descriptors that
    /// used to carry a port field of their own - a server app, a project server - so their port stops
    /// being invisible to the registry and starts being defended like any other.
    /// <para>
    /// Idempotent per <paramref name="ownerKey"/>: the owner's previous manual rows are released first,
    /// so clearing the port or moving it to another server leaves nothing behind. A null server or port
    /// therefore means "this owner claims nothing". Throws when the port belongs to somebody else,
    /// because silently skipping would leave a descriptor claiming a port the registry says is taken.
    /// </para>
    /// </summary>
    Task SyncOwnedPortAsync(
        int? serverId, int? port, string ownerKey, string ownerLabel, int? projectId,
        CancellationToken ct = default);

    /// <summary>The server's allocation window, reporting the fleet default when it has no row.</summary>
    Task<PortRangeDto> GetPortRangeAsync(int serverId, CancellationToken ct = default);

    /// <summary>Sets the window. Refuses an inverted or out-of-bounds one rather than storing it.</summary>
    Task<PortRangeDto> SetPortRangeAsync(int serverId, PortRangeDto range, CancellationToken ct = default);

    /// <summary>
    /// Reserves <paramref name="count"/> free ports of the server's window for one owner and returns
    /// them, ordered. Free means claimed by nobody and not seen listening: allocation is the one place
    /// where an observation DOES decide, because handing out a port something is already bound to would
    /// produce the very failure the registry exists to prevent.
    /// <para>
    /// The ports are not contiguous by construction, and the reservation is written immediately under
    /// <paramref name="ownerKey"/>: the unique index on (server, port, source) is what makes two
    /// concurrent allocations unable to return the same port, so a caller must treat the result as
    /// already taken.
    /// </para>
    /// Throws when the window has fewer free ports than asked, naming how many remain.
    /// </summary>
    Task<List<int>> AllocateAsync(
        int serverId, int count, string ownerKey, string ownerLabel, int? projectId,
        CancellationToken ct = default);
}
