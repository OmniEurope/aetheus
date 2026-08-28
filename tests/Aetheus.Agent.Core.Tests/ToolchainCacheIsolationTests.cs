// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.InteropServices;
using Aetheus.Agent.Core.Executors;

namespace Aetheus.Agent.Core.Tests;

public sealed class ToolchainCacheIsolationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aetheus-toolchain-cache-").FullName;

    [Fact]
    public void BuildCacheNamespace_IsolatesTrustDomainAndArchitecture()
    {
        var projectOne = ToolchainCacheManager.BuildNamespace("org-1-project-1", Architecture.X64);
        var projectTwo = ToolchainCacheManager.BuildNamespace("org-1-project-2", Architecture.X64);
        var arm = ToolchainCacheManager.BuildNamespace("org-1-project-1", Architecture.Arm64);

        Assert.NotEqual(projectOne, projectTwo);
        Assert.NotEqual(projectOne, arm);
        Assert.StartsWith("x64-", projectOne, StringComparison.Ordinal);
        Assert.StartsWith("arm64-", arm, StringComparison.Ordinal);
    }

    [Fact]
    public void PruneCacheEntries_BoundsCountAndBytesWithoutDeletingCurrentCache()
    {
        var current = CreateEntry("current", 4, DateTime.UtcNow);
        _ = CreateEntry("oldest", 4, DateTime.UtcNow.AddHours(-3));
        _ = CreateEntry("middle", 4, DateTime.UtcNow.AddHours(-2));
        _ = CreateEntry("newest", 4, DateTime.UtcNow.AddHours(-1));

        ToolchainCacheManager.PruneEntries(_root, current, maxEntries: 3, maxBytes: 10);

        Assert.True(Directory.Exists(current));
        Assert.False(Directory.Exists(Path.Combine(_root, "oldest")));
        Assert.False(Directory.Exists(Path.Combine(_root, "middle")));
        Assert.True(Directory.Exists(Path.Combine(_root, "newest")));
    }

    private string CreateEntry(string name, int bytes, DateTime lastWrite)
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        File.WriteAllBytes(Path.Combine(path, "cache.bin"), new byte[bytes]);
        Directory.SetLastWriteTimeUtc(path, lastWrite);
        return path;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
