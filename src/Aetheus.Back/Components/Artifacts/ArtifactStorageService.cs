// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Components.Artifacts;

public sealed class ArtifactStorageService(IConfiguration configuration) : IArtifactStorageService
{
    private readonly string _basePath = configuration.GetValue("ArtifactStorage:BasePath", "./data/artifacts")!;

    private string SafeResolvePath(string filePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, filePath));
        // Compare against the base path WITH a trailing separator so a sibling prefix
        // (e.g. "/data/artifacts-evil" vs base "/data/artifacts") cannot satisfy the guard.
        var basePrefix = Path.GetFullPath(_basePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal detected");
        return fullPath;
    }

    public async Task<(string RelativePath, string Sha256)> SaveArtifactAsync(int projectId, int pipelineId, int runId, string fileName, Stream content, CancellationToken ct = default)
    {
        var normalized = fileName.Replace('\\', '/');
        var safeName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(safeName) || safeName != normalized)
            throw new BadRequestException("Invalid artifact file name.");
        var relativePath = Path.Combine(projectId.ToString(), pipelineId.ToString(), runId.ToString(), safeName);
        // Route through SafeResolvePath like the read/delete paths (closes the audit-noted asymmetry).
        // Safe already (int ids + normalized name), but the traversal guard is now applied uniformly.
        var fullPath = SafeResolvePath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Compute the SHA-256 of the exact bytes written, in a single streaming pass (no re-read):
        // the CryptoStream tees the copy into SHA256 while forwarding to the file.
        using var sha = SHA256.Create();
        await using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        await using (var cryptoStream = new CryptoStream(fileStream, sha, CryptoStreamMode.Write, leaveOpen: true))
        {
            await content.CopyToAsync(cryptoStream, ct).ConfigureAwait(false);
            await cryptoStream.FlushFinalBlockAsync(ct).ConfigureAwait(false);
        }

        return (relativePath, Convert.ToHexStringLower(sha.Hash!));
    }

    public Task DeleteArtifactAsync(string filePath, CancellationToken ct = default)
    {
        var fullPath = SafeResolvePath(filePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);

            var dir = Path.GetDirectoryName(fullPath)!;
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir, recursive: true);
        }
        return Task.CompletedTask;
    }

    public Stream? OpenArtifact(string filePath)
    {
        var fullPath = SafeResolvePath(filePath);
        return File.Exists(fullPath) ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
    }

    public long GetArtifactSize(string filePath)
    {
        var fullPath = SafeResolvePath(filePath);
        return File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
    }
}
