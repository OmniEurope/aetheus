// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text.Json;

namespace Aetheus.Back.Components.Artifacts;

/// <summary>What a begin call recorded, so every later part is checked against the same intent.</summary>
/// <param name="RunId">The run the upload belongs to; a part naming another run is refused.</param>
/// <param name="TotalParts">How many parts complete the artifact.</param>
/// <param name="Sha256">The digest of the whole artifact, verified before it is published.</param>
public sealed record ChunkedUploadSession(
    string UploadId,
    int RunId,
    string Name,
    string? StageName,
    int TotalParts,
    string Sha256,
    DateTime CreatedAt)
{
    /// <summary>When a part was last accepted, or the creation time. A gigabyte-scale transfer takes
    /// time, so expiry measures inactivity rather than age: an upload still receiving parts is live.</summary>
    public DateTime LastActivityAt { get; init; } = CreatedAt;
}

/// <summary>The outcome of accepting one part.</summary>
/// <param name="Accepted">False when the part was refused; <paramref name="Reason"/> says why.</param>
/// <param name="ReceivedParts">How many distinct parts the session now holds.</param>
public sealed record ChunkedPartResult(bool Accepted, int ReceivedParts, string? Reason = null);

public interface IChunkedArtifactUploadService
{
    Task<ChunkedUploadSession> BeginAsync(
        int runId, string name, string? stageName, int totalParts, string sha256, CancellationToken ct = default);

    Task<ChunkedPartResult> AcceptPartAsync(
        int runId, string uploadId, int index, string partSha256, Stream body, CancellationToken ct = default);

    /// <summary>Assembles the parts, verifies the declared digest, and returns a stream over the
    /// result. The caller publishes it; the session is discarded either way.</summary>
    Task<Stream> OpenCompletedAsync(int runId, string uploadId, CancellationToken ct = default);

    void Discard(string uploadId);

    /// <summary>Deletes sessions older than <paramref name="maxAge"/>. Returns how many were removed.</summary>
    int PurgeExpired(TimeSpan maxAge);
}

/// <summary>
/// Artifact upload in parts, for payloads a single request cannot carry.
///
/// The browser runtime archive is about 1.14 GB, over the 1 GiB request ceiling, and the pipelines
/// worked around it by splitting the file into four named artifacts and reassembling them with
/// `cat` in three different YAML files. That put the integrity of a deployed payload in a shell
/// pipeline nothing verified. Here the digest of every part and of the whole are declared up front
/// and checked on arrival, and a part that fails mid-network is simply re-sent: the session keeps
/// the parts it already has.
///
/// Sessions live on disk under the artifact store, not in memory, so a backend restart mid-upload
/// costs the parts still in flight rather than the whole transfer.
/// </summary>
public sealed class ChunkedArtifactUploadService(IConfiguration configuration, TimeProvider timeProvider)
    : IChunkedArtifactUploadService
{
    private const string ManifestName = "manifest.json";

    /// <summary>Beside the artifacts, not inside a project directory: an unfinished upload belongs to
    /// no project yet, and must not be counted against a project's quota or served by a download.</summary>
    private readonly string _root = Path.Combine(
        configuration.GetValue("ArtifactStorage:BasePath", "./data/artifacts")!, ".uploads");

    public async Task<ChunkedUploadSession> BeginAsync(
        int runId, string name, string? stageName, int totalParts, string sha256, CancellationToken ct = default)
    {
        if (totalParts is < 1 or > 10_000)
            throw new BadRequestException("An upload must declare between 1 and 10000 parts.");
        if (!IsSha256(sha256))
            throw new BadRequestException("The artifact digest must be 64 hexadecimal characters.");

        var session = new ChunkedUploadSession(
            Guid.NewGuid().ToString("N"), runId, name, stageName, totalParts,
            sha256.ToLowerInvariant(), timeProvider.GetUtcNow().UtcDateTime);

        var directory = SessionDirectory(session.UploadId);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, ManifestName), JsonSerializer.Serialize(session), ct).ConfigureAwait(false);
        return session;
    }

    public async Task<ChunkedPartResult> AcceptPartAsync(
        int runId, string uploadId, int index, string partSha256, Stream body, CancellationToken ct = default)
    {
        var session = await ReadSessionAsync(runId, uploadId, ct).ConfigureAwait(false);
        if (index < 0 || index >= session.TotalParts)
            return new ChunkedPartResult(false, CountParts(uploadId), "Part index is outside the declared range.");
        if (!IsSha256(partSha256))
            return new ChunkedPartResult(false, CountParts(uploadId), "The part digest must be 64 hexadecimal characters.");

        var directory = SessionDirectory(uploadId);
        var partPath = Path.Combine(directory, PartName(index));
        var temporaryPath = $"{partPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            string digest;
            using (var sha = SHA256.Create())
            await using (var file = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await using (var crypto = new CryptoStream(file, sha, CryptoStreamMode.Write, leaveOpen: true))
            {
                await body.CopyToAsync(crypto, ct).ConfigureAwait(false);
                await crypto.FlushFinalBlockAsync(ct).ConfigureAwait(false);
                digest = Convert.ToHexStringLower(sha.Hash!);
            }

            // The part that arrived is not the part that was announced: keep whatever is already
            // stored and let the agent re-send. Accepting it would corrupt the artifact silently.
            if (!string.Equals(digest, partSha256, StringComparison.OrdinalIgnoreCase))
                return new ChunkedPartResult(false, CountParts(uploadId), "The part digest does not match its content.");

            // A resent part replaces the stored one: it is the same bytes by the check above.
            File.Move(temporaryPath, partPath, overwrite: true);
            // The session is alive, so the expiry clock restarts: a slow transfer of many parts must
            // not be swept away halfway through by an expiry counted from its first request.
            await File.WriteAllTextAsync(
                Path.Combine(directory, ManifestName),
                JsonSerializer.Serialize(session with { LastActivityAt = timeProvider.GetUtcNow().UtcDateTime }),
                ct).ConfigureAwait(false);
            return new ChunkedPartResult(true, CountParts(uploadId));
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task<Stream> OpenCompletedAsync(int runId, string uploadId, CancellationToken ct = default)
    {
        var session = await ReadSessionAsync(runId, uploadId, ct).ConfigureAwait(false);
        var directory = SessionDirectory(uploadId);

        var missing = Enumerable.Range(0, session.TotalParts)
            .Where(index => !File.Exists(Path.Combine(directory, PartName(index))))
            .ToList();
        if (missing.Count > 0)
            throw new BadRequestException(
                $"Upload is incomplete: {missing.Count} of {session.TotalParts} parts are missing.");

        var assembledPath = Path.Combine(directory, "assembled");
        string digest;
        using (var sha = SHA256.Create())
        await using (var output = new FileStream(
            assembledPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        await using (var crypto = new CryptoStream(output, sha, CryptoStreamMode.Write, leaveOpen: true))
        {
            for (var index = 0; index < session.TotalParts; index++)
            {
                await using var part = new FileStream(
                    Path.Combine(directory, PartName(index)), FileMode.Open, FileAccess.Read,
                    FileShare.Read, 81920, useAsync: true);
                await part.CopyToAsync(crypto, ct).ConfigureAwait(false);
            }
            await crypto.FlushFinalBlockAsync(ct).ConfigureAwait(false);
            digest = Convert.ToHexStringLower(sha.Hash!);
        }

        // Every part matched its own digest and the whole still does not match: the parts are
        // individually intact but not the artifact that was announced (a wrong order, a wrong count,
        // a resumed upload of a different build). Refuse rather than publish it.
        if (!string.Equals(digest, session.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Discard(uploadId);
            throw new BadRequestException("The assembled artifact does not match the declared digest.");
        }

        // Deleted on close by the caller's disposal, so the assembled copy never outlives publication.
        return new FileStream(
            assembledPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
    }

    public void Discard(string uploadId)
    {
        var directory = SessionDirectory(uploadId);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    public int PurgeExpired(TimeSpan maxAge)
    {
        if (!Directory.Exists(_root)) return 0;

        var cutoff = timeProvider.GetUtcNow().UtcDateTime - maxAge;
        var removed = 0;
        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            // The session's own recorded activity, not the file system's timestamps: those come from
            // a different clock than the one this service is given, and a test or a host whose clock
            // moved would purge a live transfer.
            var manifest = Path.Combine(directory, ManifestName);
            if (!File.Exists(manifest))
            {
                // A directory with no manifest was never a session (a crash between mkdir and write).
                Directory.Delete(directory, recursive: true);
                removed++;
                continue;
            }

            ChunkedUploadSession? session;
            try { session = JsonSerializer.Deserialize<ChunkedUploadSession>(File.ReadAllText(manifest)); }
            catch (JsonException) { session = null; }
            if (session is not null && session.LastActivityAt > cutoff) continue;

            Directory.Delete(directory, recursive: true);
            removed++;
        }
        return removed;
    }

    private async Task<ChunkedUploadSession> ReadSessionAsync(int runId, string uploadId, CancellationToken ct)
    {
        var manifest = Path.Combine(SessionDirectory(uploadId), ManifestName);
        if (!File.Exists(manifest)) throw new NotFoundException("Unknown or expired upload.");

        var session = JsonSerializer.Deserialize<ChunkedUploadSession>(
            await File.ReadAllTextAsync(manifest, ct).ConfigureAwait(false))
            ?? throw new NotFoundException("Unknown or expired upload.");

        // The controller already proved the caller runs THIS run; binding the session to it closes
        // the remaining gap, an agent using another run's upload id to publish into that run.
        if (session.RunId != runId) throw new NotFoundException("Unknown or expired upload.");
        return session;
    }

    /// <summary>The session directory, refusing an id that is not the opaque token this service
    /// hands out. Without it an id like <c>../../</c> would reach the file system.</summary>
    private string SessionDirectory(string uploadId)
    {
        if (uploadId.Length != 32 || !uploadId.All(char.IsAsciiHexDigitLower))
            throw new BadRequestException("Invalid upload id.");
        return Path.Combine(_root, uploadId);
    }

    private int CountParts(string uploadId)
    {
        var directory = SessionDirectory(uploadId);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "part-*").Count()
            : 0;
    }

    private static string PartName(int index) => $"part-{index:D5}";

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);
}
