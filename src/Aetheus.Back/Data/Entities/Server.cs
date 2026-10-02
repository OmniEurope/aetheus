// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

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
    public int? AgentProtocolVersion { get; set; }
    [MaxLength(8192)]
    public string? AgentCapabilitiesJson { get; set; }
    public bool AgentUpdateReserved { get; set; }
    public DateTime? AgentUpdateReservedAt { get; set; }
    public ServerStatus Status { get; set; } = ServerStatus.Offline;
    public ServerType Type { get; set; } = ServerType.Normal;
    public DateTime LastHeartbeat { get; set; }
    /// <summary>Current agent process lifetime holding the exclusive polling lease.</summary>
    public string? AgentSessionId { get; set; }
    /// <summary>Monotone fencing token incremented whenever a different agent session acquires the lease.</summary>
    public long AgentSessionFencingToken { get; set; }
    /// <summary>UTC expiry of the current agent polling lease.</summary>
    public DateTime? AgentSessionLeaseExpiresAt { get; set; }
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

    /// <summary>ADR-024 4.1: auto-detected agent capability - the patch-manage sudoers drop-in
    /// (<c>/etc/sudoers.d/aetheus-patch</c>) is present, so the agent can APPLY pending OS updates via
    /// <c>apt-get upgrade</c> under the controlled-sudo recipe. Derived every heartbeat from the reported
    /// sudoers hashes (self-heals both ways); gates the Apply-updates API. Pending-update visibility does
    /// not require it (the dry-run probe is unprivileged).</summary>
    public bool PatchManagementAvailable { get; set; }

    /// <summary>ADR-024 4.2: auto-detected agent capability - the firewall-manage sudoers drop-in
    /// (<c>/etc/sudoers.d/aetheus-firewall</c>) is present, so the agent can mutate ufw rules via the
    /// root-owned helper. Derived every heartbeat from the reported sudoers hashes (self-heals both ways);
    /// gates the open/close/toggle API. Firewall visibility also depends on it (ufw status needs root).</summary>
    public bool FirewallManagementAvailable { get; set; }

    /// <summary>PLAN-005 lot 2: the agent publishes <c>ports.observe</c>, so it can report which TCP
    /// ports are listening. Derived every heartbeat from the published capabilities, not from a sudoers
    /// grant: the scan is unprivileged, and what actually varies is the agent version.</summary>
    public bool PortObservationAvailable { get; set; }

    /// <summary>When this host's listening ports were last observed; null when never scanned. Stored on
    /// the server because a host listening on nothing yields no reservation row, and the UI must still
    /// tell "scanned, nothing found" apart from "never scanned".</summary>
    public DateTime? PortsObservedAt { get; set; }

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
    /// Null until the first heartbeat with sudoers hashes arrives. Re-captured from the heartbeat that
    /// confirms an agent update, because the update re-renders the drop-ins (recette R2-023).
    /// </summary>
    [MaxLength(2048)]
    public string? SudoersBaseline { get; set; }

    /// <summary>
    /// Recette R2-023: SHA-256 hex fingerprint of the drifted sudoers state (file name and reported hash
    /// of every drop-in that differs from <see cref="SudoersBaseline"/>) that last raised a Sec-Audit
    /// alert. The same drifted state is alerted once, across heartbeats and backend restarts; a different
    /// drifted state alerts again. Null while the drop-ins match the baseline.
    /// </summary>
    [MaxLength(64)]
    public string? SudoersDriftAlertedFingerprint { get; set; }

    /// <summary>
    /// S-TECH-CDUI: the agent's most recent capability diagnostics (a drop-in present but unreadable, a
    /// sudo probe that failed, …), persisted so an operator can see WHY a capability is OFF from the UI -
    /// not only the backend log. Stored as a compact JSON array of strings; null/empty when the last
    /// heartbeat reported none (self-heals both ways every heartbeat).
    /// </summary>
    [MaxLength(4096)]
    public string? CapabilityDiagnosticsJson { get; set; }

    /// <summary>
    /// Recette R-479: fingerprint of each heartbeat inventory section as last written, with the time of
    /// the last full rewrite, so a heartbeat rewrites only the sections that changed. Written in the same
    /// transaction as the sections; null until the first heartbeat, and ignored once older than
    /// <c>HeartbeatInventoryFingerprints.FullRewriteInterval</c> (every section is then rewritten once).
    /// </summary>
    [MaxLength(2048)]
    public string? HeartbeatInventoryFingerprintsJson { get; set; }
    public string? ScannerCapabilitiesJson { get; set; }

    /// <summary>
    /// PLAN-004 R-11: UTC instant the server was retired (the Delete action). A retired server keeps
    /// its row and every link (port registry, environments, pools, project-server chain, RBAC grants,
    /// tags, settings) but is hidden by the <see cref="ServerQueryFilters.ExcludeRetired"/> query
    /// filter from lists, selectors and pipeline dispatch, and its agent tokens are revoked. Null for
    /// an active server. Re-enrolling the same machine clears it (same id, links intact); only the
    /// explicit purge erases the row.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// PLAN-004 R-11: SHA-256 hex of the host's stable machine identity (Linux <c>/etc/machine-id</c>,
    /// Windows <c>MachineGuid</c>), reported at enrollment; the raw value never leaves the host.
    /// Enrollment matches it before the hostname, so a reinstalled machine revives its retired row.
    /// Null for servers enrolled by an agent that predates the field.
    /// </summary>
    [MaxLength(64)]
    public string? MachineIdHash { get; set; }

    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    // Navigation
    public List<ServerToken> Tokens { get; set; } = [];
    public List<ServerTask> Tasks { get; set; } = [];
    public List<AgentUpdateRequest> AgentUpdateRequests { get; set; } = [];
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
    public CertbotState? CertbotState { get; set; }
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
