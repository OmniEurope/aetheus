// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Plugins;

/// <summary>
/// Base interface for all agent plugins. Plugins can extend agent capabilities
/// by providing custom executors, collectors, or notifiers.
/// </summary>
public interface IAgentPlugin
{
    string Name { get; }
    string Version { get; }
    Task InitializeAsync(CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
}
