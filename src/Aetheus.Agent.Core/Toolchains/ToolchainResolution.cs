// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;

namespace Aetheus.Agent.Core.Toolchains;

public sealed record ToolchainCacheMount(string Name, string Target, string Key);

public sealed record ToolchainResolution
{
    public const string InfrastructureMismatchCode = TaskFailureCodes.InfrastructureMismatch;
    public const int InfrastructureMismatchExitCode = 86;

    public bool IsSuccess { get; init; }
    public string Image { get; init; } = string.Empty;
    public string Shell { get; init; } = "bash";
    public string? Toolchain { get; init; }
    public string? Version { get; init; }
    public string? NativeVersionFile { get; init; }
    public string? NativeLockFile { get; init; }
    public string? FailureReason { get; init; }
    public IReadOnlyList<ToolchainCacheMount> Caches { get; init; } = [];

    public static ToolchainResolution Failure(string reason) => new()
    {
        FailureReason = reason
    };

    public string BuildPreflightScript()
    {
        if (Toolchain is null || Version is null)
            return string.Empty;

        var probe = Toolchain switch
        {
            "dotnet" => "dotnet --version",
            "node" => "node --version | sed 's/^v//'",
            "java" => "java -version 2>&1 | sed -n 's/.*version \"\\([^\"]*\\)\".*/\\1/p'",
            "python" => "python --version 2>&1 | awk '{print $2}'",
            _ => throw new InvalidOperationException($"Unsupported toolchain '{Toolchain}'.")
        };

        return $"""
            AETHEUS_TOOLCHAIN_ACTUAL="$({probe})"
            if [ "$AETHEUS_TOOLCHAIN_ACTUAL" != "{Version}" ]; then
              echo "InfrastructureMismatch: toolchain '{Toolchain}' expected version '{Version}' but image reported '$AETHEUS_TOOLCHAIN_ACTUAL'." >&2
              exit {InfrastructureMismatchExitCode}
            fi
            """.ReplaceLineEndings("\n");
    }

    public string BuildProvenanceLog()
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            toolchain = Toolchain,
            version = Version,
            image = Image,
            shell = Shell,
            nativeVersionFile = NativeVersionFile,
            nativeLockFile = NativeLockFile,
            caches = Caches.Select(cache => new { cache.Name, cache.Target, cache.Key })
        });
        return $"##aetheus[toolchain-provenance]{payload}";
    }
}
