// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Data.Entities;

public class Server
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;

    /// <summary>
    /// Structured OS family derived from <see cref="OsDescription"/>. Set at registration and
    /// self-healed on heartbeat (when still <see cref="OsType.Unknown"/>). Pipeline stages with an
    /// <c>os:</c> requirement only resolve to servers whose <c>OsType</c> matches.
    /// </summary>
    public OsType OsType { get; set; } = OsType.Unknown;

    public string IpAddress { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public ServerStatus Status { get; set; } = ServerStatus.Offline;
    public ServerType Type { get; set; } = ServerType.Normal;
    public DateTime LastHeartbeat { get; set; }
    public string Tags { get; set; } = string.Empty; // JSON array
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// UTC timestamp captured at the moment the agent binary was last installed or upgraded.
    /// Reported by the agent each heartbeat (read from <c>$WORK_DIR/.installed-at</c>, falling
    /// back to the assembly's last-write mtime). Null until the first heartbeat after upgrade
    /// - older agents simply omit the field.
    /// </summary>
    public DateTime? AgentInstalledAt { get; set; }

    /// <summary>
    /// Secure-by-default opt-in: when <c>false</c>, this server is invisible to the pipeline
    /// scheduler - no stage will ever resolve a target to it, even if its tags/pool/environment
    /// match. Defaults to <c>false</c> on enrollment; flipped to <c>true</c> by an admin via the
    /// server detail UI once they've confirmed the server is meant to run pipeline shell.
    /// Filtering happens at scheduling time only (<c>FindOnlineServer*</c> in the pipeline repo);
    /// the F-EXEC-1 authorization gate (<c>FindServerIds*</c>) is NOT filtered, so a user who
    /// flips this on cannot retroactively bypass the Server.Admin requirement.
    /// </summary>
    public bool PipelineRunnerEnabled { get; set; }

    /// <summary>Auto-detected agent capability: the host has a working Docker CLI. Refreshed every
    /// heartbeat. Gates container-isolated pipeline stages - a <c>isolation: container</c> request
    /// resolves only to servers where this is true (otherwise the run is blocked, never downgraded).</summary>
    public bool DockerAvailable { get; set; }

    /// <summary>S-FEAT-W8KN: auto-detected agent capability - the server-management module's
    /// package-manage sudoers drop-in (<c>/etc/sudoers.d/aetheus-package</c>) is present, so the
    /// agent can install/uninstall OS packages via the controlled-sudo recipe. Derived every heartbeat
    /// from the reported sudoers hashes; gates the install/uninstall API so the UI never offers an
    /// action the agent could not perform.</summary>
    public bool PackageManagementAvailable { get; set; }

    /// <summary>PLAN-006 4.1: auto-detected agent capability - the patch-manage sudoers drop-in
    /// (<c>/etc/sudoers.d/aetheus-patch</c>) is present, so the agent can APPLY pending OS updates via
    /// <c>apt-get upgrade</c> under the controlled-sudo recipe. Derived every heartbeat from the reported
    /// sudoers hashes (self-heals both ways); gates the Apply-updates API. Pending-update visibility does
    /// not require it (the dry-run probe is unprivileged).</summary>
    public bool PatchManagementAvailable { get; set; }

    /// <summary>PLAN-006 4.2: auto-detected agent capability - the firewall-manage sudoers drop-in
    /// (<c>/etc/sudoers.d/aetheus-firewall</c>) is present, so the agent can mutate ufw rules via the
    /// root-owned helper. Derived every heartbeat from the reported sudoers hashes (self-heals both ways);
    /// gates the open/close/toggle API. Firewall visibility also depends on it (ufw status needs root).</summary>
    public bool FirewallManagementAvailable { get; set; }

    /// <summary>Cross-agent deploy capability - the deployment module's sudoers drop-in
    /// (<c>/etc/sudoers.d/aetheus-deploy</c>) is present, so the agent can apply build artifacts
    /// (binary flip + root-owned restart helper, or container compose up) on this host. Derived every
    /// heartbeat from the reported sudoers hashes (self-heals both ways); gates deploy-stage targeting
    /// so the scheduler only dispatches a <c>type: deploy</c> step to a deployment-capable server.</summary>
    public bool DeploymentTargetAvailable { get; set; }

    /// <summary>S-FEAT-W8KN: auto-detected agent capability - the mail-setup sudoers drop-in
    /// (<c>/etc/sudoers.d/aetheus-mail</c>) is present, so the agent can run the root-owned mail-setup
    /// helper via the controlled-sudo recipe. Derived every heartbeat from the reported sudoers hashes
    /// (self-heals both ways); gates the mail-setup API so the UI never offers an action the agent could
    /// not perform.</summary>
    public bool MailSetupAvailable { get; set; }

    /// <summary>Auto-detected agent capability - the teamspeak-setup sudoers drop-in
    /// (<c>/etc/sudoers.d/aetheus-teamspeak</c>) is present, so the agent can run the root-owned
    /// teamspeak-setup helper (install the TS3 server) via the controlled-sudo recipe. Derived every
    /// heartbeat from the reported sudoers hashes (self-heals both ways); gates the teamspeak-install
    /// action so the UI never offers an install the agent could not perform.</summary>
    public bool TeamspeakSetupAvailable { get; set; }

    /// <summary>Admin policy: this runner only accepts container-isolated steps. Off by default.
    /// Enforced at scheduling time, independent of what a pipeline YAML requests.</summary>
    public bool RequireContainerIsolation { get; set; }

    /// <summary>S-DES-23: agent reported it runs with TLS validation disabled (AllowInsecureCerts,
    /// dev only). Refreshed every heartbeat; surfaced as a discreet warning badge on the server card.</summary>
    public bool InsecureTls { get; set; }

    /// <summary>
    /// S-TECH-15: baseline of the agent's sudoers drop-in file hashes, captured on the first
    /// heartbeat that reports any. Stored as a compact JSON object (<c>{"aetheus-apache":"ABC…"}</c>).
    /// A later heartbeat whose hash for the same file differs raises a Sec-Audit drift alert.
    /// Null until the first heartbeat with sudoers hashes arrives.
    /// </summary>
    [MaxLength(2048)]
    public string? SudoersBaseline { get; set; }

    /// <summary>
    /// S-TECH-CDUI: the agent's most recent capability diagnostics (a drop-in present but unreadable, a
    /// sudo probe that failed, …), persisted so an operator can see WHY a capability is OFF from the UI -
    /// not only the backend log. Stored as a compact JSON array of strings; null/empty when the last
    /// heartbeat reported none (self-heals both ways every heartbeat).
    /// </summary>
    [MaxLength(4096)]
    public string? CapabilityDiagnosticsJson { get; set; }

    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    // Navigation
    public List<ServerToken> Tokens { get; set; } = [];
    public List<ServerTask> Tasks { get; set; } = [];
    public List<ServerMetric> Metrics { get; set; } = [];
    public List<ServiceInfo> Services { get; set; } = [];
    public List<DockerContainer> DockerContainers { get; set; } = [];
    public List<DockerImage> DockerImages { get; set; } = [];
    public List<DockerComposeStack> DockerComposeStacks { get; set; } = [];
    public List<DockerNetwork> DockerNetworks { get; set; } = [];
    public List<DockerVolume> DockerVolumes { get; set; } = [];
    public ApacheState? ApacheState { get; set; }
    public List<ApacheModule> ApacheModules { get; set; } = [];
    public List<ApacheVirtualHost> ApacheVirtualHosts { get; set; } = [];
    public List<CertbotCertificate> CertbotCertificates { get; set; } = [];
    public MailState? MailState { get; set; }
    public List<MailDomain> MailDomains { get; set; } = [];
    public TeamspeakState? TeamspeakState { get; set; }
    public List<TeamspeakChannel> TeamspeakChannels { get; set; } = [];
    public List<TeamspeakClient> TeamspeakClients { get; set; } = [];
    public List<TeamspeakBan> TeamspeakBans { get; set; } = [];
    public PortsentryState? PortsentryState { get; set; }
    public List<PortsentryBlockedIp> PortsentryBlockedIps { get; set; } = [];
    public List<PortsentryWhitelistIp> PortsentryWhitelistIps { get; set; } = [];
    public RkhunterState? RkhunterState { get; set; }
    public SecurityUpdatesState? SecurityUpdatesState { get; set; }
    public FirewallState? FirewallState { get; set; }
    public List<RkhunterWarning> RkhunterWarnings { get; set; } = [];
    public List<RkhunterScanResult> RkhunterScanResults { get; set; } = [];
    public List<ModuleLink> ModuleLinks { get; set; } = [];
    public List<ServerModule> Modules { get; set; } = [];
    public List<ServerApp> Apps { get; set; } = [];
    public List<ProjectServer> ProjectServers { get; set; } = [];
}
