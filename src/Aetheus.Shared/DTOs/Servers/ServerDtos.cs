// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Validation;

namespace Aetheus.Shared.DTOs;

public record ServerDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Hostname { get; init; } = string.Empty;
    public string OsDescription { get; init; } = string.Empty;
    public string AgentVersion { get; init; } = string.Empty;
    public ServerStatus Status { get; init; }
    public ServerType Type { get; init; }
    public DateTime LastHeartbeat { get; init; }
    public List<string> Tags { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public int OrganizationId { get; init; }
    /// <summary>UTC instant the agent binary was last (re)installed (the "last update"
    /// timestamp shown on the agent page). Null when the agent hasn't reported it yet -
    /// older agents that predate the marker file simply omit the field.</summary>
    public DateTime? AgentInstalledAt { get; init; }

    /// <summary>Whether this server is allowed to execute pipeline steps. Off by default on
    /// enrollment (secure-by-default); flipped on by an admin via the server detail UI. When
    /// off, the scheduler never resolves a stage target to this server even if tags/pool match.
    /// S-TECH-CUNK: tri-state on the list/tile - <c>null</c> = UNKNOWN (agent never phoned home yet),
    /// distinct from a reported <c>false</c>.</summary>
    public bool? PipelineRunnerEnabled { get; init; }

    /// <summary>Auto-detected by the agent (<c>docker --version</c> succeeds). Drives whether a
    /// stage requesting <c>isolation: container</c> can run here - a container request on a
    /// Docker-less runner is blocked (fail-closed), never silently downgraded to process mode.</summary>
    public bool DockerAvailable { get; init; }

    /// <summary>S-FEAT-W8KN: the agent has the package-manage controlled-sudo capability, so the
    /// server can install/uninstall managed OS packages. Derived every heartbeat from the reported
    /// sudoers drop-ins; the UI gates the install/uninstall actions on it.
    /// S-TECH-CUNK: tri-state on the list/tile - <c>null</c> = UNKNOWN (agent never phoned home yet).</summary>
    public bool? PackageManagementAvailable { get; init; }

    /// <summary>PLAN-006 4.1: the agent has the patch-manage controlled-sudo capability (the argv-exact
    /// <c>aetheus-patch</c> drop-in granting <c>apt-get upgrade</c>), so the fleet can APPLY pending OS
    /// updates. Derived every heartbeat from the reported sudoers drop-ins; the UI gates the Apply action
    /// on it. Pending-update VISIBILITY (the badge) does not require it - the dry-run probe is unprivileged.
    /// S-TECH-CUNK: tri-state - <c>null</c> = UNKNOWN (agent never phoned home yet).</summary>
    public bool? PatchManagementAvailable { get; init; }

    /// <summary>PLAN-006 4.2: the agent has the firewall-manage controlled-sudo capability (the root-owned
    /// <c>aetheus-firewall</c> helper grant), so the fleet can mutate ufw rules. Derived every heartbeat from
    /// the reported sudoers drop-ins; the UI gates the open/close/toggle actions on it. Firewall VISIBILITY
    /// (status + rules) does not require it - <c>ufw status</c> is read via a read-only path.
    /// S-TECH-CUNK: tri-state - <c>null</c> = UNKNOWN (agent never phoned home yet).</summary>
    public bool? FirewallManagementAvailable { get; init; }

    /// <summary>S-FEAT-W8KN: the agent has the mail-setup controlled-sudo capability (the root-owned
    /// mail-setup helper grant is present), so the server can run a full mail-server setup. Derived every
    /// heartbeat from the reported sudoers drop-ins; the UI gates the mail Setup action on it.</summary>
    public bool MailSetupAvailable { get; init; }

    /// <summary>The agent has the teamspeak-setup controlled-sudo capability (the root-owned teamspeak-setup
    /// helper grant is present), so the server can install a full TeamSpeak 3 server. Derived every
    /// heartbeat from the reported sudoers drop-ins; the UI gates the teamspeak Install action on it.</summary>
    public bool TeamspeakSetupAvailable { get; init; }

    /// <summary>Cross-agent deploy capability - the deployment module's controlled-sudo grant is present,
    /// so this server can be the target of a <c>type: deploy</c> step. Derived every heartbeat from the
    /// reported sudoers drop-ins; drives the "Déploiement" badge and the deploy-stage targeting gate.
    /// S-TECH-CUNK: tri-state on the list/tile - <c>null</c> = UNKNOWN (agent never phoned home yet).</summary>
    public bool? DeploymentTargetAvailable { get; init; }

    /// <summary>Admin policy: when true, this runner refuses any pipeline step that is NOT
    /// container-isolated. Off by default. Lets an operator enforce "containers only" on a
    /// sensitive runner regardless of what a pipeline YAML requests.</summary>
    public bool RequireContainerIsolation { get; init; }

    /// <summary>S-DES-23: the agent runs with <c>AllowInsecureCerts</c> (TLS validation disabled -
    /// dev only). Reported every heartbeat; drives a discreet "TLS non vérifié" badge on the server card.</summary>
    public bool InsecureTls { get; init; }

    /// <summary>S-TECH-CDUI: the agent's most recent capability diagnostics (e.g. a sudoers drop-in present
    /// but unreadable, a sudo probe that failed) - persisted server-side and surfaced in the UI so an
    /// operator can see WHY a capability is OFF without SSHing. Empty when the last heartbeat reported none.</summary>
    public List<string> CapabilityDiagnostics { get; init; } = [];
    public StorageDiagnosticsDto StorageDiagnostics { get; init; } = new();
}

public sealed record ServerDetailDto : ServerDto
{
    public string IpAddress { get; init; } = string.Empty;
    public double CpuPercent { get; init; }
    public double MemoryUsedMb { get; init; }
    public double MemoryTotalMb { get; init; }
    public double DiskUsedGb { get; init; }
    public double DiskTotalGb { get; init; }
    [MaxLength(2048)]
    public List<ServiceInfoDto> Services { get; init; } = [];
    public List<ServerTaskDto> RecentTasks { get; init; } = [];
    public DockerDataDto Docker { get; init; } = new();
    public ApacheDataDto Apache { get; init; } = new();
    public CertbotDataDto Certbot { get; init; } = new();
    public CronDataDto Cron { get; init; } = new();
    public MailDataDto Mail { get; init; } = new();
    public TeamspeakDataDto Teamspeak { get; init; } = new();
    public PortsentryDataDto Portsentry { get; init; } = new();
    public RkhunterDataDto Rkhunter { get; init; } = new();

    /// <summary>S-TECH-N8R3: recent metric-report timestamps (UTC, oldest→newest) over the last
    /// ~30 minutes. Each entry is a heartbeat the server sent; the UI buckets these into a real
    /// "uptime % (30 min)" and a sparkline instead of inferring liveness from a single LastHeartbeat.</summary>
    public List<DateTime> HeartbeatHistory { get; init; } = [];
}

public sealed record ServerHeartbeatDto
{
    [StringLength(50)]
    public string? AgentVersion { get; init; }
    /// <summary>UTC instant of the agent binary's last (re)install. Read once at agent
    /// startup from <c>$WORK_DIR/.installed-at</c>, with assembly mtime as fallback.</summary>
    public DateTime? AgentInstalledAt { get; init; }
    /// <summary>True when the agent host has a working Docker CLI (<c>docker --version</c> exit 0).
    /// Reported every heartbeat so the capability self-heals if Docker is installed/removed.</summary>
    public bool DockerAvailable { get; init; }
    /// <summary>Agent-reported: the pipeline-runner toolchain (.NET SDK + git) is installed. Nullable
    /// so a pre-feature agent (which omits it) reads as null = "unknown" rather than "absent".
    /// Reported every heartbeat; the backend self-heals the runner gate down when it goes false.</summary>
    public bool? PipelineRunnerAvailable { get; init; }
    /// <summary>S-DES-23: agent runs with TLS validation disabled (AllowInsecureCerts, dev only).</summary>
    public bool InsecureTls { get; init; }
    public double CpuPercent { get; init; }
    public double MemoryUsedMb { get; init; }
    public double MemoryTotalMb { get; init; }
    [MaxLength(64)]
    public List<DiskInfoDto> Disks { get; init; } = [];
    [MaxLength(2048)]
    public List<ServiceInfoDto> Services { get; init; } = [];
    public DockerDataDto Docker { get; init; } = new();
    public ApacheDataDto Apache { get; init; } = new();
    public CertbotDataDto Certbot { get; init; } = new();
    public CronDataDto Cron { get; init; } = new();
    public MailDataDto Mail { get; init; } = new();
    public TeamspeakDataDto Teamspeak { get; init; } = new();
    public PortsentryDataDto Portsentry { get; init; } = new();
    public RkhunterDataDto Rkhunter { get; init; } = new();
    public SecurityUpdatesDataDto SecurityUpdates { get; init; } = new();
    public FirewallDataDto Firewall { get; init; } = new();

    /// <summary>
    /// S-TECH-15: SHA256 hashes of the agent's sudoers drop-in files
    /// (<c>/etc/sudoers.d/aetheus-*</c>), keyed by file name. Empty when the agent
    /// has no sudoers grants or can't read them. The backend captures a baseline on
    /// first report and raises a Sec-Audit alert if a hash later diverges.
    /// </summary>
    [BoundedDictionary(32, 100, 64)]
    public Dictionary<string, string> SudoersHashes { get; init; } = [];

    /// <summary>
    /// Whether <see cref="SudoersHashes"/> comes from a completed inventory (possibly empty).
    /// Null identifies legacy agents; an empty legacy payload is not authoritative because it also
    /// represented a timeout or unreadable inventory.
    /// </summary>
    public bool? SudoersInventoryAvailable { get; init; }

    /// <summary>
    /// S-FEAT-HBDX / S-TECH-CAPX: agent-reported capability diagnostics that explain WHY a derived
    /// capability shows OFF despite the grant being installed - e.g. "sudoers drop-in aetheus-package
    /// present but unreadable" (the agent lacks the read ACL, so the hash never reaches the heartbeat) or
    /// "passwordless sudo unavailable" (a real <c>sudo -n -l</c> probe failed, so the sandbox blocks
    /// elevation even where a grant exists). Empty when nothing is amiss. Lets an operator see the real
    /// cause without SSHing to the box.
    /// </summary>
    [MaxLength(32)]
    public List<string> CapabilityDiagnostics { get; init; } = [];
    public StorageDiagnosticsDto StorageDiagnostics { get; init; } = new();
}

/// <summary>
/// Heartbeat reply. <see cref="RenewedToken"/> is non-null only when the server
/// rolled the agent's Bearer token (it was near expiry). It is plaintext and
/// returned exactly once - the agent must persist and swap it. Empty otherwise,
/// so older agents that ignore the body are unaffected.
/// </summary>
public sealed record ServerHeartbeatResponseDto
{
    [StringLength(500)]
    public string? RenewedToken { get; init; }
    public DateTime? RenewedTokenExpiresAtUtc { get; init; }
}

public sealed record DiskInfoDto
{
    [StringLength(260)]
    public string MountPoint { get; init; } = string.Empty;
    public double UsedGb { get; init; }
    public double TotalGb { get; init; }
}

public sealed record ServerRegistrationRequest
{
    [Required]
    [StringLength(500)]
    public string RegistrationToken { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Hostname { get; init; } = string.Empty;

    [StringLength(200)]
    public string OsDescription { get; init; } = string.Empty;

    [StringLength(50)]
    public string AgentVersion { get; init; } = string.Empty;

    [StringLength(45)]
    public string IpAddress { get; init; } = string.Empty;

    /// <summary>UTC instant of the agent binary install/upgrade - same source as the
    /// matching field on <see cref="ServerHeartbeatDto"/>.</summary>
    public DateTime? AgentInstalledAt { get; init; }

    /// <summary>True when the agent host has a working Docker CLI. Captured at enrollment so the
    /// capability is known before the first heartbeat.</summary>
    public bool DockerAvailable { get; init; }

    /// <summary>Agent-reported: the pipeline-runner toolchain (.NET SDK + git) is installed. Defaults
    /// the per-server PipelineRunnerEnabled gate at enrollment. Nullable so a pre-feature agent reads
    /// as null and keeps the legacy "enabled" default instead of being wrongly disabled.</summary>
    public bool? PipelineRunnerAvailable { get; init; }

    /// <summary>S-DES-23: agent runs with TLS validation disabled (AllowInsecureCerts, dev only).</summary>
    public bool InsecureTls { get; init; }
}

public sealed record ServerRegistrationResponse
{
    public int ServerId { get; init; }
    public string BearerToken { get; init; } = string.Empty;
}

/// <summary>
/// Outcome of an attempt to contact a server's agent. Agent communication is
/// poll-based (the agent pushes heartbeats); reachability is therefore derived
/// from heartbeat freshness rather than a synchronous round-trip.
/// </summary>
public sealed record ContactAgentResultDto
{
    /// <summary>True when a fresh heartbeat was observed within the freshness window.</summary>
    public bool Reachable { get; init; }

    /// <summary>Last heartbeat timestamp known to the server (UTC), if the agent ever reported.</summary>
    public DateTime? LastHeartbeat { get; init; }

    /// <summary>Seconds elapsed since the last heartbeat at the time of the check.</summary>
    public double? SecondsSinceLastHeartbeat { get; init; }

    /// <summary>Agent version from the last heartbeat, when known.</summary>
    [StringLength(50)]
    public string? AgentVersion { get; init; }

    /// <summary>Human-readable, localisation-key-free reason when <see cref="Reachable"/> is false.</summary>
    [StringLength(500)]
    public string? Error { get; init; }
}

public sealed record UpdateServerRequest
{
    [StringLength(100)]
    public string? Name { get; init; }
    [MaxLength(20)]
    [MaxItemStringLength(50)]
    public List<string>? Tags { get; init; }
    public ServerStatus? Status { get; init; }
    public ServerType? Type { get; init; }
}

/// <summary>
/// Toggles the pipeline-runner capability on a server. Single field - kept as a record so
/// future safeguards (reason text, scheduled-window override) can be added without breaking
/// the JSON contract.
/// </summary>
public sealed record UpdatePipelineRunnerRequest
{
    public bool Enabled { get; init; }
}

/// <summary>
/// Toggles the "containers only" policy on a runner. When enabled, the runner refuses any
/// pipeline step that is not container-isolated (enforced server-side at scheduling time).
/// </summary>
public sealed record UpdateContainerIsolationPolicyRequest
{
    public bool Required { get; init; }
}
