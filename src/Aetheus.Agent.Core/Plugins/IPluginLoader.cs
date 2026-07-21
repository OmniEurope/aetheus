// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Plugins;

public interface IPluginLoader
{
    IReadOnlyList<IAgentPlugin> LoadedPlugins { get; }
    Task LoadPluginsAsync(string pluginDirectory, CancellationToken ct = default);
    Task UnloadAllAsync(CancellationToken ct = default);
}
