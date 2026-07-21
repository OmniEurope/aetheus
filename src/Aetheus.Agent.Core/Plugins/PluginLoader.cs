// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aetheus.Agent.Core.Plugins;

public sealed class PluginLoader(ILogger<PluginLoader> logger) : IPluginLoader
{
    private readonly List<IAgentPlugin> _loadedPlugins = [];
    private readonly List<AssemblyLoadContext> _loadContexts = [];

    internal Func<string, bool> IsPluginDirectoryWritable { get; init; } = IsAgentWritable;

    public IReadOnlyList<IAgentPlugin> LoadedPlugins => _loadedPlugins.AsReadOnly();

    public async Task LoadPluginsAsync(string pluginDirectory, CancellationToken ct = default)
    {
        if (!Directory.Exists(pluginDirectory))
        {
            logger.LogInformation("Plugin directory not found: {Dir}. Skipping plugin loading.", pluginDirectory);
            return;
        }

        // Trust boundary: the manifest lives beside the DLLs it authorizes, so the hash
        // check only defends against corruption unless the directory is read-only for the
        // agent. Fail closed when the agent itself can write there - otherwise a pipeline
        // step running as the agent user could drop a DLL plus a matching manifest entry
        // and execute code in the agent process, outside the sandbox applied to steps.
        if (IsPluginDirectoryWritable(pluginDirectory))
        {
            logger.LogError(
                "Plugin directory {Dir} is writable by the agent user - plugin loading refused. " +
                "Make it root-owned/read-only (like the vetted helpers) to enable plugins.", pluginDirectory);
            return;
        }

        var manifestPath = Path.Combine(pluginDirectory, "plugins.manifest.json");
        var allowedHashes = LoadManifest(manifestPath);

        var assemblyFiles = Directory.GetFiles(pluginDirectory, "*.dll");
        foreach (var assemblyPath in assemblyFiles)
        {
            try
            {
                if (!VerifyPluginHash(assemblyPath, allowedHashes, out var verifiedBytes))
                {
                    logger.LogWarning("Plugin {Path} failed hash verification - skipped", assemblyPath);
                    continue;
                }

                var loadContext = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(assemblyPath), isCollectible: true);
                _loadContexts.Add(loadContext);

                using var stream = new MemoryStream(verifiedBytes);
                var assembly = loadContext.LoadFromStream(stream);

                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IAgentPlugin).IsAssignableFrom(t) && t is { IsInterface: false, IsAbstract: false });

                foreach (var pluginType in pluginTypes)
                {
                    if (Activator.CreateInstance(pluginType) is IAgentPlugin plugin)
                    {
                        await plugin.InitializeAsync(ct).ConfigureAwait(false);
                        _loadedPlugins.Add(plugin);
                        logger.LogInformation("Loaded plugin: {Name} v{Version}", plugin.Name, plugin.Version);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load plugin from {Path}", assemblyPath);
            }
        }
    }

    public async Task UnloadAllAsync(CancellationToken ct = default)
    {
        foreach (var plugin in _loadedPlugins)
        {
            try
            {
                await plugin.ShutdownAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error shutting down plugin {Name}", plugin.Name);
            }
        }
        _loadedPlugins.Clear();

        foreach (var ctx in _loadContexts)
            ctx.Unload();
        _loadContexts.Clear();
    }

    private Dictionary<string, string> LoadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            logger.LogWarning("No plugin manifest found at {Path} - all plugins will be rejected", manifestPath);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(manifestPath);
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var validated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, hash) in raw)
        {
            if (key.Contains('/') || key.Contains('\\') || key.Contains("..") || key != Path.GetFileName(key))
            {
                logger.LogWarning("Manifest entry '{Key}' rejected - path traversal detected", key);
                continue;
            }
            if (string.IsNullOrEmpty(hash) || hash.Length != 64 || !hash.All(c => Uri.IsHexDigit(c)))
            {
                logger.LogWarning("Manifest entry '{Key}' rejected - invalid SHA256 hash format", key);
                continue;
            }
            validated[key] = hash;
        }
        return validated;
    }

    // Direct probe rather than mode-bit inspection: it answers the exact question ("can THIS
    // process create a file here?") across Linux modes, ACLs and Windows alike.
    private static bool IsAgentWritable(string directory)
    {
        var probePath = Path.Combine(directory, $".prom-write-probe-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probePath)) { }
            File.Delete(probePath);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool VerifyPluginHash(string assemblyPath, Dictionary<string, string> allowedHashes, out byte[] fileBytes)
    {
        var fileName = Path.GetFileName(assemblyPath);
        fileBytes = [];
        if (!allowedHashes.TryGetValue(fileName, out var expectedHash))
            return false;

        fileBytes = File.ReadAllBytes(assemblyPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(fileBytes));
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            fileBytes = [];
            return false;
        }
        return true;
    }

    public static IServiceCollection AddPluginLoader(IServiceCollection services)
    {
        services.AddSingleton<PluginLoader>();
        services.AddSingleton<IPluginLoader>(provider => provider.GetRequiredService<PluginLoader>());
        return services;
    }
}
