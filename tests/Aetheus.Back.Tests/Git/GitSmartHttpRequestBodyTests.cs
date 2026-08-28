// SPDX-License-Identifier: EUPL-1.2
using System.IO.Compression;
using System.Text;
using Aetheus.Back.Components.Git;
using Microsoft.AspNetCore.Http;

namespace Aetheus.Back.Tests;

public sealed class GitSmartHttpRequestBodyTests
{
    [Fact]
    public async Task Open_Gzip_DecodesGitPayload()
    {
        var payload = Encoding.UTF8.GetBytes("00aewant 0123456789012345678901234567890123456789\n0000");
        await using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            await gzip.WriteAsync(payload, TestContext.Current.CancellationToken);
        compressed.Position = 0;

        await using var decoded = GitSmartHttpRequestBody.Open(compressed, "gzip", payload.Length);
        await using var output = new MemoryStream();
        await decoded.CopyToAsync(output, TestContext.Current.CancellationToken);

        Assert.Equal(payload, output.ToArray());
    }

    [Fact]
    public async Task Open_WhenDecompressedPayloadExceedsLimit_Throws413()
    {
        await using var body = new MemoryStream("12345"u8.ToArray());
        await using var bounded = GitSmartHttpRequestBody.Open(body, "identity", maximumDecompressedBytes: 4);

        var exception = await Assert.ThrowsAsync<BadHttpRequestException>(
            () => bounded.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
    }

    [Fact]
    public void Open_UnsupportedEncoding_Throws415()
    {
        using var body = new MemoryStream();

        var exception = Assert.Throws<BadHttpRequestException>(
            () => GitSmartHttpRequestBody.Open(body, "br", maximumDecompressedBytes: 100));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, exception.StatusCode);
    }

    [Fact]
    public async Task Open_MalformedGzip_Throws400()
    {
        await using var body = new MemoryStream("not-gzip"u8.ToArray());
        await using var decoded = GitSmartHttpRequestBody.Open(body, "gzip", maximumDecompressedBytes: 100);

        var exception = await Assert.ThrowsAsync<BadHttpRequestException>(
            () => decoded.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }
}
