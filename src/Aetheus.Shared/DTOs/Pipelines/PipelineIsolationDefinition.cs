// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.DTOs;

/// <summary>
/// Per-stage/job execution isolation. <c>Mode</c> = <c>process</c> (default) or <c>container</c>.
/// In container mode each step runs in an ephemeral, hardened container (cap-drop ALL, read-only
/// root, pids/memory limits). <c>Runtime</c> selects the container runtime (<c>runc</c> default,
/// <c>runsc</c> for gVisor, <c>kata</c> for micro-VMs) so kernel-level isolation can be enabled
/// without a code change.
/// </summary>
public sealed record PipelineIsolationDefinition
{
    public const string ModeProcess = "process";
    public const string ModeContainer = "container";

    /// <summary><c>process</c> (default) or <c>container</c>.</summary>
    public string Mode { get; init; } = ModeProcess;

    /// <summary>Immutable container image when <see cref="Mode"/> is <c>container</c>, including
    /// its SHA-256 digest. Mutually exclusive with <see cref="Toolchain"/>.</summary>
    public string? Image { get; init; }

    /// <summary>Toolchain key resolved from the checked-out repository's
    /// <c>.aetheus/toolchains.lock.yaml</c>. Mutually exclusive with <see cref="Image"/>.</summary>
    public string? Toolchain { get; init; }

    /// <summary>Explicit container shell for a direct image: <c>bash</c> (default) or <c>sh</c>.
    /// Toolchain-backed containers take the shell from the lock manifest.</summary>
    public string? Shell { get; init; }

    /// <summary>Container runtime: <c>runc</c> (default), <c>runsc</c> (gVisor), <c>kata</c>.</summary>
    public string? Runtime { get; init; }

    /// <summary>Container network: <c>bridge</c> (default - outbound access, needed for clone /
    /// package restore) or <c>none</c> to fully cut the container off from the network.</summary>
    public string? Network { get; init; }

    /// <summary>Optional memory ceiling for the container (S-UX-35), maps to <c>docker run --memory</c>
    /// (e.g. <c>512m</c>, <c>2g</c>). Null = unbounded.</summary>
    public string? Memory { get; init; }

    /// <summary>Optional CPU quota for the container (S-UX-35), maps to <c>docker run --cpus</c>
    /// (e.g. <c>1.5</c>). Null = unbounded.</summary>
    public string? Cpus { get; init; }

    public bool IsContainer => string.Equals(Mode, ModeContainer, StringComparison.OrdinalIgnoreCase);
}
