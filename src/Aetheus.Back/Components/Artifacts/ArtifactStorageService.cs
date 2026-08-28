// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;

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
        var relativeDirectory = Path.Combine(projectId.ToString(), pipelineId.ToString(), runId.ToString());
        var relativePath = Path.Combine(relativeDirectory, safeName);
        // Route through SafeResolvePath like the read/delete paths (closes the audit-noted asymmetry).
        // Safe already (int ids + normalized name), but the traversal guard is now applied uniformly.
        var fullPath = SafeResolvePath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // First stream into a unique temporary file and hash the exact bytes. The final path includes
        // both that digest and a publication id, and FileMode.CreateNew makes the move non-overwriting.
        // A retry of the same logical artifact therefore creates a new immutable object instead of
        // changing bytes referenced by an existing database row.
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Guid.NewGuid():N}.upload");
        try
        {
            using var sha = SHA256.Create();
            await using (var fileStream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await using (var cryptoStream = new CryptoStream(fileStream, sha, CryptoStreamMode.Write, leaveOpen: true))
            {
                await content.CopyToAsync(cryptoStream, ct).ConfigureAwait(false);
                await cryptoStream.FlushFinalBlockAsync(ct).ConfigureAwait(false);
            }

            var digest = Convert.ToHexStringLower(sha.Hash!);
            var extension = Path.GetExtension(safeName);
            var stem = Path.GetFileNameWithoutExtension(safeName);
            var immutableName = $"{stem}.{digest}.{Guid.NewGuid():N}{extension}";
            relativePath = Path.Combine(relativeDirectory, immutableName);
            fullPath = SafeResolvePath(relativePath);
            File.Move(temporaryPath, fullPath, overwrite: false);
            return (relativePath, digest);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
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
