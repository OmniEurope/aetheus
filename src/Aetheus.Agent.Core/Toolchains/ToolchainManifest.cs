// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Agent.Core.Toolchains;

internal sealed record ToolchainLockManifest
{
    public int Version { get; init; }
    public Dictionary<string, ToolchainLockEntry?>? Toolchains { get; init; } = [];
}

internal sealed record ToolchainLockEntry
{
    public string Version { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public string Path { get; init; } = ".";
    public string? Shell { get; init; }
    public List<ToolchainCacheDefinition?>? Caches { get; init; } = [];
}

internal sealed record ToolchainCacheDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string LockFile { get; init; } = string.Empty;
}
