// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.Servers;

/// <summary>
/// One-shot "why is this server offline?" probe. Reads cheap signals
/// (heartbeat freshness, token validity, agent vs. backend version) and
/// returns a single human-readable line plus the underlying data so the UI
/// can render a focused explanation without making the operator chase
/// <c>journalctl</c>. Split out from <see cref="IServerService"/> for
/// Interface Segregation.
/// </summary>
public interface IServerDiagnosticService
{
    Task<ServerDiagnosticDto?> DiagnoseAsync(int serverId, CancellationToken ct = default);
}
