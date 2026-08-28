// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aetheus.Agent.Core.Toolchains;

public sealed class ToolchainResolver : IToolchainResolver
{
    internal const string ManifestRelativePath = ".aetheus/toolchains.lock.yaml";

    private static readonly IDeserializer ManifestDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public async Task<ToolchainResolution> ResolveAsync(
        ContainerSpec spec,
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spec.Toolchain))
            return ResolveDirectImage(spec);
        var specificationError = ValidateSpecification(spec);
        if (specificationError is not null) return specificationError;
        var manifestPath = ResolveRepositoryPath(workspacePath, ManifestRelativePath);
        if (manifestPath is null || !File.Exists(manifestPath))
            return ToolchainResolution.Failure(
                $"Toolchain '{spec.Toolchain}' requires '{ManifestRelativePath}' in the checked-out repository.");
        try
        {
            var manifest = await ReadManifestAsync(workspacePath, manifestPath, cancellationToken).ConfigureAwait(false);
            var entryError = ResolveManifestEntry(manifest, spec.Toolchain, out var entry);
            return entryError ?? await ResolveLockedToolchainAsync(
                spec.Toolchain, entry!, workspacePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or YamlException)
        {
            return ToolchainResolution.Failure(
                $"Could not read '{ManifestRelativePath}': {ex.Message}");
        }
    }

    private static ToolchainResolution? ValidateSpecification(ContainerSpec spec)
    {
        if (!ContainerExecutionContractValidator.IsToolchainName(spec.Toolchain))
            return ToolchainResolution.Failure($"Toolchain key '{spec.Toolchain}' is invalid.");
        return !string.IsNullOrWhiteSpace(spec.Image)
            ? ToolchainResolution.Failure("Container isolation must declare either 'image' or 'toolchain', not both.")
            : null;
    }

    private static async Task<ToolchainLockManifest> ReadManifestAsync(
        string workspacePath,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        EnsureNoLinks(workspacePath, manifestPath);
        var yaml = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        return ManifestDeserializer.Deserialize<ToolchainLockManifest>(yaml)
               ?? throw new YamlException("The manifest is empty.");
    }

    private static ToolchainResolution? ResolveManifestEntry(
        ToolchainLockManifest manifest,
        string toolchain,
        out ToolchainLockEntry? entry)
    {
        entry = null;
        if (manifest.Version != 1)
            return ToolchainResolution.Failure(
                $"Unsupported toolchain manifest version '{manifest.Version}' (expected 1).");
        if (manifest.Toolchains is null)
            return ToolchainResolution.Failure(
                $"Toolchain manifest '{ManifestRelativePath}' has a null toolchains collection.");
        var entries = new Dictionary<string, ToolchainLockEntry?>(
            manifest.Toolchains, StringComparer.Ordinal);
        if (!entries.TryGetValue(toolchain, out entry))
            return ToolchainResolution.Failure(
                $"Toolchain '{toolchain}' is not declared in '{ManifestRelativePath}'.");
        if (entry is null)
            return ToolchainResolution.Failure(
                $"Toolchain '{toolchain}' has a null manifest entry.");
        var contractError = ValidateEntry(toolchain, entry);
        return contractError is null ? null : ToolchainResolution.Failure(contractError);
    }

    private static async Task<ToolchainResolution> ResolveLockedToolchainAsync(
        string toolchain,
        ToolchainLockEntry entry,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        var toolchainRoot = ResolveRepositoryPath(workspacePath, entry.Path);
        if (toolchainRoot is null || !Directory.Exists(toolchainRoot))
            return ToolchainResolution.Failure(
                $"Toolchain '{toolchain}' path '{entry.Path}' does not exist in the repository.");
        try
        {
            EnsureNoLinks(workspacePath, toolchainRoot);
            var native = await ReadNativeContractAsync(
                toolchain, workspacePath, toolchainRoot, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(native.Version, entry.Version, StringComparison.Ordinal))
                return ToolchainResolution.Failure(
                    $"Toolchain '{toolchain}' declares version '{entry.Version}' but " +
                    $"'{native.VersionFile}' requires '{native.Version}'.");
            var caches = await ResolveCachesAsync(
                toolchain, entry, workspacePath, toolchainRoot, cancellationToken).ConfigureAwait(false);
            return caches.Error ?? CreateSuccess(toolchain, entry, workspacePath, native, caches.Mounts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or JsonException or InvalidOperationException)
        {
            return ToolchainResolution.Failure(
                $"Toolchain '{toolchain}' could not be resolved: {ex.Message}");
        }
    }

    private static async Task<CacheResolution> ResolveCachesAsync(
        string toolchain,
        ToolchainLockEntry entry,
        string workspacePath,
        string toolchainRoot,
        CancellationToken cancellationToken)
    {
        var mounts = new List<ToolchainCacheMount>(entry.Caches?.Count ?? 0);
        foreach (var cache in entry.Caches ?? [])
        {
            if (cache is null)
                return CacheResolution.Failure($"Toolchain '{toolchain}' cache contract contains a null entry.");
            var cacheError = ValidateCache(cache);
            if (cacheError is not null)
                return CacheResolution.Failure($"Toolchain '{toolchain}' cache contract is invalid: {cacheError}");
            var lockPath = ResolveRepositoryPath(toolchainRoot, cache.LockFile);
            if (lockPath is null || !File.Exists(lockPath))
                return CacheResolution.Failure(
                    $"Toolchain '{toolchain}' cache '{cache.Name}' requires lock file '{cache.LockFile}'.");
            EnsureNoLinks(workspacePath, lockPath);
            var digest = await ComputeSha256Async(lockPath, cancellationToken).ConfigureAwait(false);
            mounts.Add(new ToolchainCacheMount(cache.Name, cache.Target, $"{toolchain}-{entry.Version}-{digest}"));
        }
        return new CacheResolution(mounts, null);
    }

    private static ToolchainResolution CreateSuccess(
        string toolchain,
        ToolchainLockEntry entry,
        string workspacePath,
        NativeToolchainContract native,
        IReadOnlyList<ToolchainCacheMount> caches) => new()
        {
            IsSuccess = true,
            Image = entry.Image,
            Shell = entry.Shell ?? "bash",
            Toolchain = toolchain,
            Version = entry.Version,
            NativeVersionFile = Path.GetRelativePath(workspacePath, native.VersionFile).Replace('\\', '/'),
            NativeLockFile = caches.Count == 0 ? null : entry.Caches![0]!.LockFile.Replace('\\', '/'),
            Caches = caches
        };

    private static ToolchainResolution ResolveDirectImage(ContainerSpec spec)
    {
        if (!ContainerExecutionContractValidator.IsImmutableImage(spec.Image))
            return ToolchainResolution.Failure(
                "Container images must be immutable references ending in '@sha256:<64 hex characters>'.");
        if (!ContainerExecutionContractValidator.IsSupportedShell(spec.Shell))
            return ToolchainResolution.Failure(
                $"Container shell '{spec.Shell}' is unsupported (expected 'bash' or 'sh').");
        return new ToolchainResolution
        {
            IsSuccess = true,
            Image = spec.Image,
            Shell = spec.Shell ?? "bash"
        };
    }

    private static string? ValidateEntry(string name, ToolchainLockEntry entry)
    {
        if (name is not ("dotnet" or "node" or "java" or "python"))
            return $"Toolchain '{name}' has no supported native contract reader.";
        if (!ContainerExecutionContractValidator.IsExactVersion(entry.Version))
            return $"Toolchain '{name}' version '{entry.Version}' is not exact.";
        if (!ContainerExecutionContractValidator.IsImmutableImage(entry.Image))
            return $"Toolchain '{name}' image must end in an immutable SHA-256 digest.";
        if (!ContainerExecutionContractValidator.IsSupportedShell(entry.Shell))
            return $"Toolchain '{name}' shell '{entry.Shell}' is unsupported.";
        return null;
    }

    private static string? ValidateCache(ToolchainCacheDefinition cache)
    {
        if (!ContainerExecutionContractValidator.IsToolchainName(cache.Name))
            return $"cache name '{cache.Name}' is invalid.";
        if (string.IsNullOrWhiteSpace(cache.Target)
            || !cache.Target.StartsWith("/", StringComparison.Ordinal)
            || cache.Target.Contains("..", StringComparison.Ordinal)
            || cache.Target.Any(char.IsWhiteSpace))
            return $"cache target '{cache.Target}' must be an absolute container path.";
        if (string.IsNullOrWhiteSpace(cache.LockFile))
            return $"cache '{cache.Name}' has no lock_file.";
        return null;
    }

    private static async Task<NativeToolchainContract> ReadNativeContractAsync(
        string toolchain,
        string workspacePath,
        string toolchainRoot,
        CancellationToken cancellationToken)
    {
        return toolchain switch
        {
            "dotnet" => await ReadDotnetAsync(workspacePath, toolchainRoot, cancellationToken)
                .ConfigureAwait(false),
            "node" => await ReadNodeAsync(workspacePath, toolchainRoot, cancellationToken)
                .ConfigureAwait(false),
            "java" => await ReadJavaAsync(workspacePath, toolchainRoot, cancellationToken)
                .ConfigureAwait(false),
            "python" => await ReadPythonAsync(workspacePath, toolchainRoot, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported toolchain '{toolchain}'.")
        };
    }

    private static async Task<NativeToolchainContract> ReadDotnetAsync(
        string workspacePath,
        string root,
        CancellationToken cancellationToken)
    {
        var path = RequireFile(workspacePath, root, "global.json");
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("sdk", out var sdk)
            || sdk.ValueKind != JsonValueKind.Object
            || !sdk.TryGetProperty("version", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("global.json requires a string sdk.version property.");
        var version = versionElement.GetString();
        return Native(path, version);
    }

    private static async Task<NativeToolchainContract> ReadNodeAsync(
        string workspacePath,
        string root,
        CancellationToken cancellationToken)
    {
        var path = RequireFile(workspacePath, root, "package.json");
        _ = RequireFile(workspacePath, root, "package-lock.json");
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("engines", out var engines)
            || engines.ValueKind != JsonValueKind.Object
            || !engines.TryGetProperty("node", out var versionElement)
            || versionElement.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("package.json requires a string engines.node property.");
        var version = versionElement.GetString();
        return Native(path, version);
    }

    private static async Task<NativeToolchainContract> ReadJavaAsync(
        string workspacePath,
        string root,
        CancellationToken cancellationToken)
    {
        var path = RequireFile(workspacePath, root, ".java-version");
        _ = RequireFile(workspacePath, root, "pom.xml");
        _ = RequireFile(workspacePath, root, "mvnw");
        var version = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
        return Native(path, version);
    }

    private static async Task<NativeToolchainContract> ReadPythonAsync(
        string workspacePath,
        string root,
        CancellationToken cancellationToken)
    {
        var path = RequireFile(workspacePath, root, ".python-version");
        _ = RequireFile(workspacePath, root, "pyproject.toml");
        if (!new[] { "uv.lock", "requirements.lock", "poetry.lock" }
            .Any(candidate => File.Exists(Path.Combine(root, candidate))))
            throw new InvalidOperationException("Python requires uv.lock, requirements.lock, or poetry.lock.");
        var version = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
        return Native(path, version);
    }

    private static NativeToolchainContract Native(string path, string? version)
    {
        if (!ContainerExecutionContractValidator.IsExactVersion(version))
            throw new InvalidOperationException(
                $"Native version file '{path}' does not contain an exact supported version.");
        return new NativeToolchainContract(version!, path);
    }

    private static string RequireFile(string workspacePath, string root, string relativePath)
    {
        var path = ResolveRepositoryPath(root, relativePath);
        if (path is null || !File.Exists(path))
            throw new InvalidOperationException($"Required native file '{relativePath}' is missing.");
        EnsureNoLinks(workspacePath, path);
        return path;
    }

    private static string? ResolveRepositoryPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return null;
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        return candidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private static void EnsureNoLinks(string workspacePath, string targetPath)
    {
        var root = Path.GetFullPath(workspacePath);
        var relative = Path.GetRelativePath(root, targetPath);
        var current = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(
                    $"Repository contract path '{relative}' traverses a symbolic link.");
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(digest);
    }

    private sealed record NativeToolchainContract(string Version, string VersionFile);

    private sealed record CacheResolution(
        IReadOnlyList<ToolchainCacheMount> Mounts,
        ToolchainResolution? Error)
    {
        public static CacheResolution Failure(string message) =>
            new([], ToolchainResolution.Failure(message));
    }
}
