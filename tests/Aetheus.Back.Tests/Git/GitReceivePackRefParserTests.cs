// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using Aetheus.Back.Components.Git;

namespace Aetheus.Back.Tests;

public sealed class GitReceivePackRefParserTests
{
    private const string OldCommit = "0123456789abcdef0123456789abcdef01234567";
    private const string NewCommit = "89abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task CopyAndExtractUpdatedRefsAsync_ExtractsChangedBranchesAndPreservesTheRequest()
    {
        var request = BuildRequest(
            $"{OldCommit} {NewCommit} refs/heads/main\0report-status\n",
            $"{OldCommit} {OldCommit} refs/heads/develop\n",
            $"{OldCommit} {NewCommit} refs/tags/v1.0.0\n");
        await using var source = new MemoryStream(request);
        await using var destination = new MemoryStream();

        var refs = await GitReceivePackRefParser.CopyAndExtractUpdatedRefsAsync(source, destination, TestContext.Current.CancellationToken);

        var update = Assert.Single(refs);
        Assert.Equal("refs/heads/main", update.Reference);
        Assert.Equal(NewCommit, update.NewObjectId);
        Assert.Equal(request, destination.ToArray());
    }

    [Fact]
    public async Task CopyAndExtractUpdatedRefsAsync_DeletionDoesNotTrigger()
    {
        var request = BuildRequest($"{OldCommit} {new string('0', 40)} refs/heads/main\n");
        await using var source = new MemoryStream(request);
        await using var destination = new MemoryStream();

        var refs = await GitReceivePackRefParser.CopyAndExtractUpdatedRefsAsync(source, destination, TestContext.Current.CancellationToken);

        Assert.Empty(refs);
    }

    [Fact]
    public async Task CopyAndExtractUpdatedRefsAsync_MalformedPreamble_ForwardsWithoutTriggering()
    {
        var request = Encoding.ASCII.GetBytes("zzzznot-a-packet");
        await using var source = new MemoryStream(request);
        await using var destination = new MemoryStream();

        var refs = await GitReceivePackRefParser.CopyAndExtractUpdatedRefsAsync(source, destination, TestContext.Current.CancellationToken);

        Assert.Empty(refs);
        Assert.Equal(request, destination.ToArray());
    }

    private static byte[] BuildRequest(params string[] commands)
    {
        var bytes = new List<byte>();
        foreach (var command in commands)
        {
            var payload = Encoding.UTF8.GetBytes(command);
            bytes.AddRange(Encoding.ASCII.GetBytes((payload.Length + 4).ToString("x4")));
            bytes.AddRange(payload);
        }
        bytes.AddRange("0000"u8.ToArray());
        bytes.AddRange([1, 2, 3, 4]);
        return bytes.ToArray();
    }
}
