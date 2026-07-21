// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

/// <summary>
/// F-32: Typed operations the agent can execute without free-form shell strings.
/// Each value maps to a dedicated <c>IOperationExecutor</c> on the agent that uses
/// <c>ProcessStartInfo.ArgumentList</c> directly - no shell interpolation, no allow-list regex.
/// Free-form pipeline scripts continue to use <c>IExecutor</c> (the escape hatch).
/// </summary>
public enum OperationKind
{
    None = 0,

    // Docker
    DockerRestartContainer = 100,
    DockerStartContainer = 101,
    DockerStopContainer = 102,
    DockerPullImage = 103,

    // systemd / service
    ServiceStart = 200,
    ServiceStop = 201,
    ServiceRestart = 202,
    ServiceStatus = 203,

    /// <summary>
    /// Read the last N journal lines of a systemd unit via <c>journalctl -u &lt;unit&gt; -n &lt;lines&gt;
    /// --no-pager [-f]</c> (argv-only, no shell interpolation - replaces the former shell-string logs
    /// task). Target is the unit name (<c>ServiceNameRegex</c>); line count and follow flag ride in
    /// <see cref="Aetheus.Shared.Constants.ServiceLogsEnv"/>. No sudo (journal read).
    /// </summary>
    ServiceGetLogs = 205,

    /// <summary>
    /// Phase 3 (option B): enable a systemd unit (start-on-boot) via the controlled-sudo recipe.
    /// Target is the unit name (validated against <c>ServiceNameRegex</c>); the agent normalises a
    /// trailing <c>.service</c> and invokes <c>sudo -n /bin/systemctl enable &lt;unit&gt;</c>. The
    /// argv-exact <c>/etc/sudoers.d/aetheus-service-enable</c> drop-in bounds it to a FIXED list
    /// of pre-existing units (no wildcards), satisfying the security doc's "operate only on a fixed
    /// list of pre-existing units" exception to the otherwise-forbidden <c>systemctl enable</c>.
    /// </summary>
    ServiceEnable = 204,

    /// <summary>
    /// S-FEAT-W8KN: install an OS package on a managed server via the controlled-sudo recipe. Target
    /// is the package name (validated against the closed <see cref="Aetheus.Shared.Constants.ManageablePackages"/>
    /// allow-list); the agent invokes <c>sudo -n /usr/bin/apt-get install -y &lt;pkg&gt;</c> against the
    /// argv-exact <c>/etc/sudoers.d/aetheus-package</c> drop-in (a package outside the list is
    /// refused by sudo at the OS level). Requires the server-management module's package-manage capability.
    /// </summary>
    ServiceInstall = 210,

    /// <summary>S-FEAT-W8KN: uninstall an OS package - <c>sudo -n /usr/bin/apt-get remove -y &lt;pkg&gt;</c>.
    /// Same allow-list and sudoers drop-in as <see cref="ServiceInstall"/>.</summary>
    ServiceUninstall = 211,

    /// <summary>
    /// PLAN-006 4.1: apply pending OS security/package updates via the controlled-sudo recipe. Runs in
    /// two modes carried in <c>AETHEUS_PATCH_DRY_RUN</c>: a non-mutating dry-run (<c>apt-get -s upgrade</c>,
    /// no sudo, lists what would change) and an apply (<c>sudo -n /usr/bin/apt-get -y upgrade</c> against
    /// the argv-exact <c>/etc/sudoers.d/aetheus-patch</c> drop-in). Before applying, the agent runs the
    /// dry-run and ABORTS honestly if any <see cref="Aetheus.Shared.Constants.CriticalPackages"/> (or a
    /// per-server override) would be upgraded - never a fake success. Target is unused. Requires the
    /// server-management module's patch-manage capability.
    /// </summary>
    SystemPackageUpgrade = 212,

    // Apache
    ApacheReload = 300,
    ApacheTestConfig = 301,

    /// <summary>
    /// Item #8 of the plan: typed read of the apache error/access log. Target is the log type
    /// (<c>"error"</c> or <c>"access"</c>). Lines count is fixed at the agent (100) - the
    /// front-side preferences flow through the env vars, not the target string, so the
    /// operation-target validator can keep its short whitelist.
    /// </summary>
    ApacheGetLogs = 302,

    /// <summary>Item #5.2/#5.3 - service control via the controlled-sudo recipe (item #6).
    /// Target is unused; the executor invokes the matching <c>systemctl</c> verb on
    /// <c>apache2.service</c> through sudo with argv exact (no wildcards).</summary>
    ApacheStart = 303,
    ApacheStop = 304,
    ApacheRestart = 305,

    /// <summary>Item #5.3 - write a vhost config file via the ACL +rw the install script
    /// grants on <c>/etc/apache2/sites-available</c> (no sudo). Target is the site filename
    /// (validated against <c>SiteNameRegex</c>); the base64-encoded content is passed via the
    /// <c>AETHEUS_APACHE_CONFIG_B64</c> environment variable to keep the task command field
    /// short and free of shell metacharacters.</summary>
    ApacheSaveConfig = 306,

    /// <summary>type: apache-proxy - composite reverse-proxy apply: write the rendered vhost to
    /// <c>sites-available/&lt;site&gt;.conf</c> (ACL +rw, no sudo), enable it by symlinking into
    /// <c>sites-enabled</c>, then apply via <c>systemctl reload apache2.service</c> (controlled-sudo),
    /// which reloads AND validates - a broken vhost fails the reload while the old config stays live.
    /// (The controlled-sudo <c>noexec</c> blocks a standalone <c>apache2ctl</c> wrapper run, so the
    /// systemd reload is the validation point.) Target is the site filename; the base64 vhost is passed
    /// in <c>AETHEUS_APACHE_CONFIG_B64</c>. Requires <c>--enable-apache-manage</c>.</summary>
    ApacheConfigureProxy = 307,

    /// <summary>Typed read of a vhost config file. Target is the site filename (validated against
    /// <c>ApacheSiteFileRegex</c>); the config root travels in <c>AETHEUS_APACHE_CONFIG_ROOT</c>.
    /// The agent reads <c>{root}/sites-available/{site}</c> (or <c>{root}/conf.d/{site}</c>) directly via
    /// <c>File.ReadAllText</c> (read ACL, no shell) - replaces the dead <c>cat "..." 2&gt;/dev/null || cat</c>
    /// builder the CommandValidator rejected.</summary>
    ApacheGetConfig = 308,

    /// <summary>Typed read of a document root's <c>.htaccess</c>. Target is the absolute document root
    /// (validated against <c>ApacheDocumentRootRegex</c>); the agent reads <c>{root}/.htaccess</c> directly
    /// via <c>File.ReadAllText</c> (empty when absent) - replaces the dead <c>cat "..." || echo ''</c>
    /// builder.</summary>
    ApacheGetHtaccess = 309,

    /// <summary>Typed write of a document root's <c>.htaccess</c>. Target is the absolute document root;
    /// the base64 content travels in <c>AETHEUS_APACHE_HTACCESS_B64</c>. The agent writes
    /// <c>{root}/.htaccess</c> via <c>File.WriteAllBytes</c> (same non-sudo access model as the old shell,
    /// minus the metacharacters) - replaces the dead <c>printf ... | base64 -d &gt; "..."</c> builder.</summary>
    ApacheSaveHtaccess = 310,

    /// <summary>type: certbot - obtain/install an HTTPS certificate for one or more domains via a
    /// root-owned controlled-sudo helper (<c>sudo -n /usr/local/lib/aetheus/aetheus-certbot-issue</c>,
    /// which re-validates the domains then runs <c>certbot certonly --apache</c>). When ACME validation
    /// cannot complete (no public DNS / unreachable :80) the helper falls back to a self-signed cert in
    /// the Let's Encrypt layout so the site still serves HTTPS. Domains/email are passed via
    /// <c>AETHEUS_CERTBOT_*</c> env vars. Requires <c>--enable-certbot-manage</c>.</summary>
    CertbotObtain = 320,

    /// <summary>type: certbot management - renew a single certificate by lineage name via the root-owned
    /// controlled-sudo helper (<c>sudo -n /usr/local/lib/aetheus/aetheus-certbot-manage renew &lt;name&gt;</c>,
    /// which re-validates the name then runs <c>certbot renew --cert-name &lt;name&gt;</c>). Target is the
    /// certificate name. Requires <c>--enable-certbot-manage</c>. Replaces the old free-form
    /// <c>sudo certbot renew</c> shell task the agent's CommandValidator rejected (GTFOBins).</summary>
    CertbotRenew = 321,

    /// <summary>Renew ALL certificates - the helper runs <c>certbot renew</c>. Target unused. Same helper
    /// and sudoers grant as <see cref="CertbotRenew"/>.</summary>
    CertbotRenewAll = 322,

    /// <summary>Delete a certificate by lineage name - the helper runs <c>certbot delete --cert-name
    /// &lt;name&gt;</c>. Target is the certificate name.</summary>
    CertbotDelete = 323,

    /// <summary>Revoke then delete a certificate by lineage name - the helper runs <c>certbot revoke
    /// --cert-name &lt;name&gt; --delete-after-revoke</c>. Target is the certificate name.</summary>
    CertbotRevoke = 324,

    // RKHunter (items #10.2/#10.3) - same controlled-sudo recipe as Apache. Target unused.
    RkhunterScan = 350,        // sudo /usr/bin/rkhunter --check --skip-keypress --nocolors --report-warnings-only
    RkhunterUpdate = 351,      // sudo /usr/bin/rkhunter --update --nocolors
    RkhunterPropupd = 352,     // sudo /usr/bin/rkhunter --propupd --nocolors

    // Agent self-management
    /// <summary>
    /// The agent downloads the latest agent build from the backend's <c>/downloads</c> endpoint
    /// and hands off to a detached platform updater (systemd unit / Windows Service) that swaps
    /// the binaries and restarts the service. The target string is unused. Handled by
    /// <c>AgentSelfUpdateOperationExecutor</c> in <c>Aetheus.Agent.Core</c>.
    /// </summary>
    AgentSelfUpdate = 400,

    // Mail (service control + queue + logs)
    MailStartPostfix = 500,
    MailStopPostfix = 501,
    MailRestartPostfix = 502,
    MailReloadPostfix = 503,
    MailStartDovecot = 504,
    MailStopDovecot = 505,
    MailRestartDovecot = 506,
    MailReloadDovecot = 507,
    MailFlushQueue = 508,
    MailViewQueue = 509,
    MailTestConfig = 510,

    /// <summary>
    /// Typed read of mail logs via <c>journalctl</c>. Target is the log source
    /// (<c>"postfix"</c> or <c>"dovecot"</c>).
    /// </summary>
    MailGetLogs = 511,

    /// <summary>
    /// S-FEAT-W8KN: full mail-stack setup (install + configure postfix/dovecot/opendkim + enable
    /// services) via the root-owned <c>aetheus/mail-setup</c> helper through an argv-exact sudoers
    /// grant - the same controlled-sudo recipe as cron. Replaces the old free-form shell pipeline that
    /// always failed for the non-root agent (no elevation). Target is the mail domain; hostname,
    /// DKIM selector, admin email and quota travel in <c>AETHEUS_MAIL_*</c> env vars (re-validated
    /// agent-side) and the admin password is piped to the helper via stdin (never argv - keeps it off
    /// the process list). Requires the server-management module's mail-setup capability.
    /// </summary>
    MailSetup = 512,

    /// <summary>
    /// S-FEAT-W8KN (incremental): add a virtual domain to an already-provisioned mail stack via the
    /// root-owned <c>aetheus/mail-manage</c> helper (sub-command <c>add-domain</c>) through the same
    /// argv-exact aetheus-mail sudoers grant as <see cref="MailSetup"/>. Target is the new domain
    /// (re-validated agent-side); no secret involved. Replaces the old free-form postconf/systemctl
    /// shell task that always failed for the non-root agent.
    /// </summary>
    MailAddDomain = 513,

    /// <summary>
    /// S-FEAT-W8KN (incremental): add a mailbox account via the <c>mail-manage</c> helper (sub-command
    /// <c>add-account</c>). Target is the account email; the parent domain and quota travel in
    /// <c>AETHEUS_MAIL_*</c> env vars and the password is piped to the helper over stdin (never argv).
    /// All re-validated agent-side before the helper, which itself re-validates and hashes the password
    /// via <c>doveadm pw</c> over stdin.
    /// </summary>
    MailAddAccount = 514,

    /// <summary>
    /// S-FEAT-W8KN (incremental): add a virtual alias via the <c>mail-manage</c> helper (sub-command
    /// <c>add-alias</c>). Target is the source email; the destination travels in a
    /// <c>AETHEUS_MAIL_*</c> env var. No secret involved.
    /// </summary>
    MailAddAlias = 515,

    /// <summary>
    /// S-FEAT-W8KN (incremental): rotate the DKIM key via the <c>mail-manage</c> helper (sub-command
    /// <c>dkim-rotate</c>). Target is the domain; the new selector travels in a <c>AETHEUS_MAIL_*</c>
    /// env var. The helper regenerates the key, rewrites KeyTable/SigningTable, restarts opendkim, and
    /// emits the public-key TXT record on stdout. No secret involved.
    /// </summary>
    MailDkimRotate = 516,

    /// <summary>
    /// S-TECH-MCPW: change a mailbox account password via the <c>mail-manage</c> helper (sub-command
    /// <c>change-password</c>). Target is the account email; the new password is piped to the helper over
    /// stdin (never argv, so it stays off <c>ps</c>/<c>/proc/cmdline</c>). Re-validated agent-side, then
    /// the helper re-validates and hashes it via <c>doveadm pw</c> over stdin. Replaces the old free-form
    /// <c>doveadm pw -p</c> shell task that exposed the password on the process argv.
    /// </summary>
    MailChangePassword = 517,

    /// <summary>Remove a mail domain via the root-owned <c>mail-manage</c> helper (<c>remove-domain
    /// &lt;domain&gt;</c>). Target is the domain. Replaces the dead shell builder whose
    /// <c>$(postconf ...) &amp;&amp;</c> chain the CommandValidator rejected.</summary>
    MailRemoveDomain = 518,

    /// <summary>Delete a mailbox account via <c>mail-manage delete-account &lt;email&gt; &lt;domain&gt;</c>.
    /// Target is the account email; the domain travels in <c>AETHEUS_MAIL_DOMAIN</c>. Replaces the dead
    /// <c>sed -i ... &amp;&amp; postmap ...</c> shell builder.</summary>
    MailDeleteAccount = 519,

    /// <summary>Remove a mail alias via <c>mail-manage remove-alias &lt;source&gt;</c>. Target is the
    /// alias source email. Replaces the dead <c>sed -i ... &amp;&amp; postmap ...</c> shell builder.</summary>
    MailRemoveAlias = 520,

    /// <summary>Read a domain's DKIM public key (DNS TXT record) via <c>mail-manage dkim-read
    /// &lt;selector&gt;</c>, which cats <c>/etc/opendkim/keys/&lt;selector&gt;.txt</c> as root. Target is the
    /// DKIM selector. Replaces the dead <c>cat ... 2&gt;/dev/null</c> builder.</summary>
    MailDkimRead = 521,

    // Portsentry (service control + status + unblock)
    PortsentryStart = 550,
    PortsentryStop = 551,
    PortsentryRestart = 552,
    PortsentryStatus = 553,

    /// <summary>
    /// Typed read of portsentry logs via <c>journalctl</c>. Target is unused.
    /// </summary>
    PortsentryGetLogs = 554,

    /// <summary>Provision/configure portsentry via the root-owned <c>portsentry-setup</c> helper
    /// (<c>sudo -n</c>, argv-exact): apt-installs portsentry, writes the scan mode into
    /// <c>/etc/default/portsentry</c> and the TCP/UDP port lists into <c>portsentry.conf</c>, then enables
    /// the service. Target is the scan mode; the port lists travel in <c>AETHEUS_PORTSENTRY_*_PORTS</c>
    /// env vars. Replaces the dead <c>sed ... 2&gt;/dev/null || true &amp;&amp; apt-get install</c> shell chain
    /// the CommandValidator rejected. Requires the portsentry-manage capability.</summary>
    PortsentrySetup = 556,

    /// <summary>
    /// Unblock an IP address from portsentry. Target is the IPv4/IPv6 address to unblock,
    /// validated by <see cref="Aetheus.Shared.Validation.OperationTargetValidator"/>.
    /// </summary>
    PortsentryUnblock = 555,

    // Firewall (PLAN-006 4.2) - ufw control via the root-owned aetheus-firewall helper (sudo -n, the
    // helper re-validates port/proto/source before running ufw). Complements Portsentry (detection) with
    // control (prevention). Requires the firewall-manage capability. Anti-lockout is a LOGIC guard: a deny
    // on the admin SSH port is refused, and the admin port is auto-allowed before ufw is enabled.
    /// <summary>Open a port: <c>ufw allow</c>. Target is the port (1-65535); protocol (tcp/udp) and source
    /// (CIDR or "any") travel in <c>AETHEUS_FIREWALL_*</c> env vars, re-validated agent- and helper-side.</summary>
    FirewallAllow = 560,

    /// <summary>Close/deny a port: <c>ufw deny</c>. Same target/env shape as <see cref="FirewallAllow"/>.
    /// Refused (anti-lockout) when it targets the admin SSH port.</summary>
    FirewallDeny = 561,

    /// <summary>Delete an existing rule: <c>ufw delete</c> the matching allow/deny. Same target/env shape.</summary>
    FirewallDeleteRule = 562,

    /// <summary>Enable or disable ufw. Target is <c>"enable"</c> or <c>"disable"</c>. On enable the helper
    /// first allows the admin SSH port so the default-deny policy can't lock the operator out.</summary>
    FirewallSetEnabled = 563,

    // Pipeline artifacts & releases
    /// <summary>
    /// Collect build artifacts from the agent's working directory. The <c>Command</c> field
    /// contains a JSON-serialized list of glob patterns to include in the zip. Environment
    /// variables carry <c>AETHEUS_ARTIFACT_NAME</c>, <c>AETHEUS_RUN_ID</c>, and
    /// <c>AETHEUS_STAGE_NAME</c>.
    /// </summary>
    PipelineCollectArtifacts = 600,

    /// <summary>
    /// Execute a release step: generate changelog from git log and POST release to backend.
    /// <c>Command</c> contains the version pattern. Environment variables carry
    /// <c>AETHEUS_PROJECT_ID</c>, <c>AETHEUS_RUN_ID</c>, <c>AETHEUS_CHANGELOG</c> (bool).
    /// </summary>
    PipelineCreateRelease = 601,

    /// <summary>
    /// Replace <c>#{VAR_NAME}#</c> tokens in target files with resolved pipeline variable values.
    /// <c>Command</c> is a JSON array of file glob patterns. All resolved pipeline variables
    /// are available in the environment variables.
    /// </summary>
    PipelineSubstituteVariables = 602,

    /// <summary>
    /// Publish Cobertura-format coverage XML to the backend. <c>Command</c> is a JSON array of
    /// glob patterns to find coverage XML files (e.g. <c>["**/coverage.cobertura.xml"]</c>).
    /// </summary>
    PipelinePublishCoverage = 603,

    /// <summary>
    /// Publish a SARIF lint report to the backend. <c>Command</c> is a JSON array of glob patterns
    /// to find SARIF files (e.g. <c>["**/*.sarif"]</c>). Counts are stored as a structured lint result.
    /// </summary>
    PipelinePublishLint = 604,

    /// <summary>
    /// L: analyze C# cyclomatic complexity + LOC over the workspace (Roslyn) and publish aggregate
    /// metrics. <c>Command</c> is unused; the agent walks the working directory for <c>*.cs</c> files.
    /// </summary>
    PipelinePublishComplexity = 605,

    /// <summary>
    /// Cross-agent deployment: apply a build artifact (or an existing release) on a deployment-capable
    /// agent. The target is the application instance name (validated against
    /// <see cref="Aetheus.Shared.Validation.OperationTargetValidator"/>'s deploy-app regex). The
    /// resolved artifact is pulled from the backend's artifact store via an agent-authorised download
    /// endpoint; deploy parameters (<c>AETHEUS_DEPLOY_*</c>) travel in environment variables. The
    /// binary mode performs an atomic symlink flip + a root-owned restart helper through an argv-exact
    /// sudoers grant (no unit generation); the container mode runs <c>docker load</c> +
    /// <c>docker compose up -d --wait</c>. Requires the deployment module's capability. Handled by
    /// <c>DeployOperationExecutor</c> in <c>Aetheus.Agent.Core</c>.
    /// </summary>
    PipelineDeploy = 606,

    /// <summary>
    /// Restore a pipeline artifact into the current run workspace. The artifact is resolved by the
    /// control plane from a trusted upstream run and the agent downloads it through the same
    /// run-authorized endpoint as deployments. Handled by <c>PipelineArtifactOperationExecutor</c>.
    /// </summary>
    PipelineRestoreArtifacts = 607,

    // App backups (PLAN-006 4.3) - dump a managed app's DB + archive its files into a retained artifact,
    // and verify recoverability with a restore-check on a throwaway target. argv-only; DB creds travel in
    // an encrypted env var and are piped/passed off the process list, never interpolated into a shell.
    /// <summary>Run a backup: dump the DB (pg_dump/mysqldump) + archive declared file paths, then upload
    /// the archive to the backend artifact store (retained). The dump/paths/engine/creds travel in
    /// <c>AETHEUS_BACKUP_*</c> env vars (creds encrypted). Target is the policy id. Handled by
    /// <c>BackupOperationExecutor</c>.</summary>
    BackupExecute = 620,

    /// <summary>Restore-check (the no-fake core): download a backup archive, restore it onto a REAL
    /// throwaway target (a dedicated scratch DB created for the test and dropped after), verify, and
    /// report pass/fail. A backup is "verified" only after this succeeds; a failure is red, never masked.
    /// Target is the backup-run id; the archive ref + engine travel in <c>AETHEUS_BACKUP_*</c> env vars.</summary>
    BackupRestoreCheck = 621,

    /// <summary>Explicit destructive restore of a previously verified backup onto its configured live
    /// database. It is only dispatched by a manual rollback pipeline after the control plane validates
    /// the backup/project/server relationship; never by the scheduler.</summary>
    BackupRestore = 622,

    // Cron (Phase 3) - typed save/delete via the aetheus-cron-apply sudo helper. The helper is the
    // security boundary: root-owned, agent-non-writable, re-validates every field, and can ONLY write
    // /etc/cron.d/aetheus-<id> (fixed dir + prefix). Replaces the old free-form shell tasks that
    // CommandValidator rejected for their pipes/subshells.
    /// <summary>
    /// Write (create/update) a cron job. <c>Command</c> carries the job id (validated as
    /// <c>CronValidation.IsValidIdentifier</c>); user/schedule/command travel in the
    /// <c>AETHEUS_CRON_USER</c> / <c>AETHEUS_CRON_SCHEDULE</c> / <c>AETHEUS_CRON_COMMAND</c>
    /// environment variables (re-validated agent-side, then passed argv-only to the helper).
    /// </summary>
    CronSave = 750,

    /// <summary>Delete a cron job. <c>Command</c> carries the job id; the helper removes
    /// <c>/etc/cron.d/aetheus-&lt;id&gt;</c>.</summary>
    CronDelete = 751,

    // TeamSpeak (S-TECH-87) - ServerQuery write/query path over native TCP instead of `nc` shell-out.
    /// <summary>
    /// S-TECH-87: run a single TeamSpeak ServerQuery command (kick/ban/move/poke, channel/server
    /// edit, snapshot, groups, tokens, complaints, …) over a native <c>TcpClient</c> instead of the
    /// old <c>printf … | nc</c> shell task. <c>Command</c> carries the inner ServerQuery line
    /// (already <c>EscapeServerQuery</c>-encoded server-side); the query port is in the
    /// <c>TEAMSPEAK_QUERY_PORT</c> environment variable. The agent reads the serveradmin credential
    /// from its local credentials file and prepends <c>login … / use sid=1</c> - no credential ever
    /// travels in the task. Handled by <c>TeamspeakServerQueryOperationExecutor</c>.
    /// </summary>
    TeamspeakServerQuery = 700,

    /// <summary>
    /// Full TeamSpeak 3 server install via the root-owned <c>aetheus/teamspeak-setup</c> helper
    /// through an argv-exact sudoers grant - the same controlled-sudo recipe as mail-setup. Replaces
    /// the old free-form shell pipeline (useradd / cat &gt; /etc/systemd / systemctl enable) that always
    /// failed for the non-root agent. Target is the install path (validated against
    /// <see cref="Aetheus.Shared.Validation.OperationTargetValidator"/>); the voice/query ports travel
    /// in <c>AETHEUS_TEAMSPEAK_*</c> env vars (re-validated agent-side). No secret involved - the
    /// helper extracts the generated serveradmin credential into the agent-readable credentials file.
    /// Requires the server-management module's teamspeak-setup capability.
    /// </summary>
    TeamspeakSetup = 701,

    /// <summary>Graceful TeamSpeak restart: broadcast a warning (gm), wait, kick everyone, then restart
    /// the <c>ts3server</c> systemd unit. Composite agent-side sequence over the native ServerQuery TCP
    /// client (gm/kick) plus <c>sudo -n systemctl restart ts3server</c> (service-control capability).
    /// Target is unused; the warning seconds/message and query port travel in <c>TEAMSPEAK_*</c> env vars.
    /// Replaces the dead <c>printf|nc &amp;&amp; sleep &amp;&amp; ... &amp;&amp; systemctl restart</c> shell chain the
    /// CommandValidator rejected. Handled by <c>TeamspeakGracefulRestartOperationExecutor</c>.</summary>
    TeamspeakGracefulRestart = 702,

    /// <summary>Typed read of the TeamSpeak server logs. Target is the install path; the agent tails the
    /// newest file under <c>{installPath}/logs/</c> directly (read access via the teamspeak group) -
    /// replaces the dead <c>tail ... | sort | tail</c> shell builder. Handled by
    /// <c>TeamspeakGracefulRestartOperationExecutor</c>.</summary>
    TeamspeakGetLogs = 703
}
