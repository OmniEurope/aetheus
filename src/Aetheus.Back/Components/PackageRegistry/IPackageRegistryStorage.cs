// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Components.PackageRegistry;

public interface IPackageRegistryStorage
{
    Task<StoredPackagePayload> SaveAsync(
        PackageRegistryKind kind,
        string normalizedName,
        string normalizedVersion,
        string extension,
        Stream content,
        CancellationToken ct = default);

    Stream? OpenRead(string relativePath);
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
    Task<int> ReconcileAsync(
        IReadOnlySet<string> referencedPaths,
        DateTime orphanCutoffUtc,
        CancellationToken ct = default);
}

public sealed record StoredPackagePayload(
    string RelativePath,
    long SizeBytes,
    string Sha256,
    string Sha1,
    string Integrity,
    bool CreatedNew = false);
