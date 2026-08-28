// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Linux.Services;

namespace Aetheus.Agent.Core.Tests;

public sealed class LinuxCredentialProtectorMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"aetheus-key-migration-{Guid.NewGuid():N}");

    [Fact]
    public void MigrateLegacyMaterial_VerifiesRestrictedCopyThenDeletesLegacyFile()
    {
        Directory.CreateDirectory(_root);
        var legacy = Path.Combine(_root, "legacy", ".agent-entropy");
        var restricted = Path.Combine(_root, "restricted", ".agent-entropy");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        var material = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        File.WriteAllBytes(legacy, material);

        var migrated = LinuxCredentialProtector.MigrateLegacyMaterial(legacy, restricted);

        Assert.Equal(material, migrated);
        Assert.Equal(material, File.ReadAllBytes(restricted));
        Assert.False(File.Exists(legacy));
    }

    [Fact]
    public void MigrateLegacyMaterial_WhenRestrictedWriteFails_PreservesLegacyFile()
    {
        Directory.CreateDirectory(_root);
        var legacy = Path.Combine(_root, "legacy", ".agent-salt");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllBytes(legacy, [1, 2, 3, 4]);
        var blockingFile = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(blockingFile, "block");

        Assert.ThrowsAny<IOException>(() => LinuxCredentialProtector.MigrateLegacyMaterial(
            legacy,
            Path.Combine(blockingFile, ".agent-salt")));
        Assert.True(File.Exists(legacy));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
