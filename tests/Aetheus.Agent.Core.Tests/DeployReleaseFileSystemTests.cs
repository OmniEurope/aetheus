// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// A deployed release must not stay world-readable, and it must not lose the executable bit on the
/// binaries that need it. <c>HardenedFileMode</c> is the single decision behind both, so it is worth
/// pinning mode by mode rather than through a real deployment.
/// </summary>
public class DeployReleaseFileSystemTests
{
    [Fact]
    public void HardenedMode_NeverGrantsAnythingToOther()
    {
        // The point of hardening: a release directory readable by every account on the host is the
        // finding this exists to prevent.
        var permissive = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                       | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                       | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        var hardened = DeployReleaseFileSystem.HardenedFileMode(permissive);

        Assert.Equal(UnixFileMode.None, hardened & UnixFileMode.OtherRead);
        Assert.Equal(UnixFileMode.None, hardened & UnixFileMode.OtherWrite);
        Assert.Equal(UnixFileMode.None, hardened & UnixFileMode.OtherExecute);
    }

    [Fact]
    public void HardenedMode_NeverGrantsGroupWrite()
    {
        var groupWritable = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;

        var hardened = DeployReleaseFileSystem.HardenedFileMode(groupWritable);

        Assert.Equal(UnixFileMode.None, hardened & UnixFileMode.GroupWrite);
    }

    [Fact]
    public void PlainDataFile_KeepsOwnerReadWriteAndGroupRead_ButNoExecute()
    {
        var dataFile = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead;

        var hardened = DeployReleaseFileSystem.HardenedFileMode(dataFile);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
            hardened);
    }

    [Theory]
    [InlineData(UnixFileMode.UserExecute)]
    [InlineData(UnixFileMode.GroupExecute)]
    [InlineData(UnixFileMode.OtherExecute)]
    public void ExecutableBitOnAnyClass_SurvivesAsUserAndGroupExecute(UnixFileMode executeBit)
    {
        // A binary that arrived executable only for "other" must still be runnable by the agent after
        // hardening; dropping the bit would break the deployed app rather than secure it.
        var hardened = DeployReleaseFileSystem.HardenedFileMode(UnixFileMode.UserRead | executeBit);

        Assert.True(hardened.HasFlag(UnixFileMode.UserExecute));
        Assert.True(hardened.HasFlag(UnixFileMode.GroupExecute));
        Assert.False(hardened.HasFlag(UnixFileMode.OtherExecute));
    }

    [Fact]
    public void HardeningIsIdempotent()
    {
        var once = DeployReleaseFileSystem.HardenedFileMode(
            UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.OtherRead);

        Assert.Equal(once, DeployReleaseFileSystem.HardenedFileMode(once));
    }

    [Fact]
    public void EmptyMode_StillYieldsAReadableWritableOwner()
    {
        // Files can arrive with no bits at all from a tarball; the agent must still be able to read
        // its own release afterwards.
        var hardened = DeployReleaseFileSystem.HardenedFileMode(UnixFileMode.None);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,
            hardened);
    }

    [Fact]
    public void ReleaseDirectoryMode_GrantsNothingToOther()
    {
        Assert.Equal(UnixFileMode.None, DeployReleaseFileSystem.ReleaseDirectoryMode & UnixFileMode.OtherRead);
        Assert.Equal(UnixFileMode.None, DeployReleaseFileSystem.ReleaseDirectoryMode & UnixFileMode.OtherWrite);
        Assert.Equal(UnixFileMode.None, DeployReleaseFileSystem.ReleaseDirectoryMode & UnixFileMode.OtherExecute);
    }

    [Fact]
    public void ReadLinkTarget_OnAPathThatIsNotALink_ReturnsNull()
    {
        var file = Path.Combine(Path.GetTempPath(), $"aetheus-deploy-{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "release");
        try
        {
            Assert.Null(DeployReleaseFileSystem.ReadLinkTarget(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ReadLinkTarget_OnAMissingPath_ReturnsNullRatherThanThrowing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"aetheus-missing-{Guid.NewGuid():N}");

        Assert.Null(DeployReleaseFileSystem.ReadLinkTarget(missing));
    }
}
