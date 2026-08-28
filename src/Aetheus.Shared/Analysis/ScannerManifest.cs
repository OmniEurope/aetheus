// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aetheus.Shared.Analysis;

public sealed record ScannerManifest
{
    public int SchemaVersion { get; init; }
    public DateTime UpdatedAt { get; init; }
    public List<ScannerManifestEntry> Scanners { get; init; } = [];
}

public sealed record ScannerManifestEntry
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Execution { get; init; } = string.Empty;
    public string? Image { get; init; }
    public string? EntryPoint { get; init; }
    public string? RuntimeImage { get; init; }
    public Dictionary<string, string> Environment { get; init; } = new(StringComparer.Ordinal);
    public string? DownloadUriLinuxAmd64 { get; init; }
    public string? Sha256LinuxAmd64 { get; init; }
    public string? Executable { get; init; }
    public string? VersionPackage { get; init; }
    public List<string> FileExtensions { get; init; } = [];
    public List<string> Arguments { get; init; } = [];
    public string? ContainerOutputDirectory { get; init; }
    public string ReportPath { get; init; } = string.Empty;
    public string ReportFormat { get; init; } = string.Empty;
    public string? RuleSetVersion { get; init; }
    public List<int> FindingExitCodes { get; init; } = [];
    public string Network { get; init; } = "none";
    public string? NetworkReason { get; init; }
    public List<string> AllowedDestinations { get; init; } = [];
    public string? CacheDirectoryName { get; init; }
    public long MaxCacheBytes { get; init; } = 2_147_483_648;
    public string? TemporaryHomeDirectory { get; init; }
    public string? HomeDirectory { get; init; }
    public string? ContainerWorkingDirectory { get; init; }
    public string? SeedTemporaryHomeFrom { get; init; }
    public string? SeedTemporaryHomeTarget { get; init; }
    public long? MaxTemporaryBytes { get; init; }
    public long MaxTemporaryHomeBytes { get; init; } = 1_073_741_824;
    public string Memory { get; init; } = "1g";
    public string Cpus { get; init; } = "1.0";
    public long MaxReportBytes { get; init; } = 104_857_600;
    public string License { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}

public static class ScannerManifestCatalog
{
    private static readonly Lazy<ScannerManifestSnapshot> DefaultManifest = new(LoadEmbedded);

    public static ScannerManifest Default => DefaultManifest.Value.Manifest;
    public static string Sha256 => DefaultManifest.Value.Sha256;

    public static ScannerManifestEntry? Find(string key) =>
        Default.Scanners.FirstOrDefault(scanner => string.Equals(scanner.Key, key, StringComparison.OrdinalIgnoreCase));

    private static ScannerManifestSnapshot LoadEmbedded()
    {
        const string suffix = ".scanner-manifest.json";
        var assembly = typeof(ScannerManifestCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded scanner manifest is unavailable.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var manifest = JsonSerializer.Deserialize(bytes, ScannerManifestJsonContext.Default.ScannerManifest)
            ?? throw new InvalidDataException("The embedded scanner manifest is empty.");
        Validate(manifest);
        return new ScannerManifestSnapshot(
            manifest,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    internal static void Validate(ScannerManifest manifest)
    {
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported scanner manifest schema.");
        if (manifest.Scanners.Count == 0) throw new InvalidDataException("Scanner manifest has no scanner.");
        if (manifest.Scanners.Select(scanner => scanner.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Scanners.Count)
            throw new InvalidDataException("Scanner manifest keys must be unique.");
        foreach (var scanner in manifest.Scanners)
        {
            ValidateIdentityAndExecution(scanner);
            ValidateStorage(scanner);
            ValidateDirectories(scanner);
        }
    }

    private static void ValidateIdentityAndExecution(ScannerManifestEntry scanner)
    {
        if (string.IsNullOrWhiteSpace(scanner.Key) || string.IsNullOrWhiteSpace(scanner.Version))
            throw new InvalidDataException("Every scanner requires a key and version.");
        if (scanner.Execution == "container"
            && (string.IsNullOrWhiteSpace(scanner.Image) || !scanner.Image.Contains("@sha256:", StringComparison.Ordinal)))
            throw new InvalidDataException($"Container scanner '{scanner.Key}' is not pinned by digest.");
        if (scanner.Execution == "binary"
            && (string.IsNullOrWhiteSpace(scanner.DownloadUriLinuxAmd64)
                || scanner.Sha256LinuxAmd64?.Length != 64
                || string.IsNullOrWhiteSpace(scanner.RuntimeImage)
                || !scanner.RuntimeImage.Contains("@sha256:", StringComparison.Ordinal)))
            throw new InvalidDataException($"Binary scanner '{scanner.Key}' lacks an immutable download hash.");
        if (scanner.Execution is not ("container" or "binary"))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an unsupported execution mode.");
        if ((scanner.FileExtensions ?? []).Any(extension => extension.Length < 2 || extension[0] != '.'))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an invalid file extension filter.");
    }

    private static void ValidateStorage(ScannerManifestEntry scanner)
    {
        if (!string.IsNullOrWhiteSpace(scanner.CacheDirectoryName)
            && scanner.CacheDirectoryName.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an invalid cache directory name.");
        if (!string.IsNullOrWhiteSpace(scanner.CacheDirectoryName)
            && (scanner.MaxCacheBytes is < 1_048_576 or > 10_737_418_240))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an invalid cache quota.");
        if (scanner.MaxTemporaryBytes is not null
            && (scanner.MaxTemporaryBytes is < 67_108_864 or > 4_294_967_296))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an invalid temporary storage quota.");
    }

    private static void ValidateDirectories(ScannerManifestEntry scanner)
    {
        if (!string.IsNullOrWhiteSpace(scanner.TemporaryHomeDirectory)
            && (IsInvalidDirectory(scanner.TemporaryHomeDirectory, "/home/")
                || scanner.MaxTemporaryHomeBytes is < 1_048_576 or > 2_147_483_648))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an invalid temporary home declaration.");
        if (!string.IsNullOrWhiteSpace(scanner.HomeDirectory)
            && IsInvalidDirectory(scanner.HomeDirectory, "/home/"))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an invalid home directory.");
        if (!string.IsNullOrWhiteSpace(scanner.ContainerWorkingDirectory)
            && IsInvalidDirectory(scanner.ContainerWorkingDirectory, "/"))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an invalid container working directory.");
        ValidateSeedDirectories(scanner);
        if (HasInvalidContainerOutputDirectory(scanner))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an invalid container output directory.");
    }

    private static bool IsInvalidDirectory(string value, string requiredPrefix) =>
        !value.StartsWith(requiredPrefix, StringComparison.Ordinal)
        || value.Contains("..", StringComparison.Ordinal);

    private static bool HasInvalidContainerOutputDirectory(ScannerManifestEntry scanner) =>
        scanner.Execution == "container"
        && !string.IsNullOrWhiteSpace(scanner.ContainerOutputDirectory)
        && (IsInvalidDirectory(scanner.ContainerOutputDirectory, "/")
            || scanner.ContainerOutputDirectory.Contains(':', StringComparison.Ordinal));

    private static void ValidateSeedDirectories(ScannerManifestEntry scanner)
    {
        if (!string.IsNullOrWhiteSpace(scanner.SeedTemporaryHomeFrom)
            && (string.IsNullOrWhiteSpace(scanner.TemporaryHomeDirectory)
                || !scanner.SeedTemporaryHomeFrom.StartsWith("/home/", StringComparison.Ordinal)
                || scanner.SeedTemporaryHomeFrom.Contains("..", StringComparison.Ordinal)))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an invalid temporary home seed.");
        if (!string.IsNullOrWhiteSpace(scanner.SeedTemporaryHomeTarget)
            && (string.IsNullOrWhiteSpace(scanner.TemporaryHomeDirectory)
                || !scanner.SeedTemporaryHomeTarget.StartsWith(
                    scanner.TemporaryHomeDirectory.TrimEnd('/') + "/", StringComparison.Ordinal)
                || scanner.SeedTemporaryHomeTarget.Contains("..", StringComparison.Ordinal)))
            throw new InvalidDataException($"Scanner '{scanner.Key}' has an invalid temporary home seed target.");
    }

    private sealed record ScannerManifestSnapshot(ScannerManifest Manifest, string Sha256);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ScannerManifest))]
internal sealed partial class ScannerManifestJsonContext : JsonSerializerContext;
