// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Servers;

/// <summary>Env-var contract for the <c>SystemPackageUpgrade</c> operation (ADR-024 4.1).</summary>
public static class PatchingConstants
{
    /// <summary>When <c>"1"</c>, the agent runs a non-mutating dry-run (<c>apt-get -s upgrade</c>) and
    /// reports what WOULD change; otherwise it applies (after the critical-package abort check).</summary>
    public const string DryRunEnvVar = "AETHEUS_PATCH_DRY_RUN";

    /// <summary>Optional per-server extra blocklist (comma-separated exact package names) layered on top of
    /// <see cref="CriticalPackages"/>. An upgrade that would touch any of these is aborted honestly.</summary>
    public const string BlocklistEnvVar = "AETHEUS_PATCH_BLOCKLIST";
}
