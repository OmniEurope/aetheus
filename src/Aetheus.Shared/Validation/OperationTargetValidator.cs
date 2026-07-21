// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Text.RegularExpressions;
using Aetheus.Shared.Constants;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.Validation;

/// <summary>
/// F-32 / Hardening: single source of truth for per-<see cref="OperationKind"/> target validation.
/// Used by the backend (controller-side rejection in <c>CreateOperation</c>) and by the agent's
/// <c>IOperationExecutor</c> dispatch (last-line defence). When this file moves, both sides move
/// together - this is the contract.
/// </summary>
public static partial class OperationTargetValidator
{
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-:/]{0,127}$")]
    public static partial Regex DockerTargetRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-@]{0,63}$")]
    public static partial Regex ServiceNameRegex();

    /// <summary>Apache vhost filename - letters/digits/dot/underscore/dash only, ends with
    /// <c>.conf</c>. No slashes (path traversal) and no whitespace. Mirrors the back-end
    /// <c>ApacheCommandHelper.SiteNameRegex</c> source-of-truth.</summary>
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-]{0,127}\.conf$")]
    public static partial Regex ApacheSiteFileRegex();

    /// <summary>Apache document root - an absolute filesystem path of letters/digits/dot/underscore/
    /// dash/slash only. The leading <c>(?!.*\.\.)</c> lookahead rejects any "<c>..</c>" segment so a
    /// GetHtaccess/SaveHtaccess target cannot traverse out of the intended tree (the agent then reads/writes
    /// <c>{root}/.htaccess</c> under it). No whitespace, no shell metacharacters.</summary>
    [GeneratedRegex(@"^/(?!.*\.\.)[a-zA-Z0-9._/-]{1,255}$")]
    public static partial Regex ApacheDocumentRootRegex();

    /// <summary>S-TECH-87: a TeamSpeak ServerQuery command line - any printable run with no control
    /// characters (CR/LF rejected to block ServerQuery line-injection). Length is bounded separately.</summary>
    [GeneratedRegex(@"^[^\x00-\x1f]+$")]
    public static partial Regex TeamspeakQueryRegex();

    /// <summary>TeamSpeak install path - an absolute filesystem path of letters/digits/dot/underscore/
    /// dash/slash only. S-TECH-TSPR: the leading <c>(?!.*\.\.)</c> lookahead rejects any "<c>..</c>"
    /// segment so the value cannot traverse out of the intended tree (the previous charclass-only form
    /// allowed "<c>..</c>", making the helper's re-validation with the SAME regex no real defence in
    /// depth). No whitespace, no shell metacharacters; the root-owned teamspeak-setup helper re-validates
    /// the same shape before it touches the filesystem.</summary>
    [GeneratedRegex(@"^/(?!.*\.\.)[a-zA-Z0-9._/-]{1,127}$")]
    public static partial Regex TeamspeakInstallPathRegex();

    /// <summary>Deploy application instance name - letters/digits/underscore/dash only, 1–64 chars. No
    /// dots, slashes or whitespace: the name is also the systemd template instance (<c>aetheus-app@&lt;name&gt;</c>)
    /// and the on-disk deploy directory (<c>…/deploy/&lt;name&gt;/</c>), so this blocks unit-name and
    /// path-traversal injection. The root-owned restart helper re-validates the same shape agent-side.</summary>
    [GeneratedRegex(@"^[a-zA-Z0-9_-]{1,64}$")]
    public static partial Regex DeployAppRegex();

    /// <summary>certbot certificate lineage name - the directory name under
    /// <c>/etc/letsencrypt/live</c>. Letters/digits/dot/underscore/dash only (no slashes, no whitespace),
    /// so it cannot traverse out of the lineage tree or inject extra certbot args. Mirrors the back-end
    /// <c>CertbotCommandHelper</c> cert-name shape; the root-owned certbot-manage helper re-validates the
    /// same shape before running certbot.</summary>
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,199}$")]
    public static partial Regex CertbotCertNameRegex();

    public static bool IsValid(OperationKind kind, string? target)
    {
        // Self-update is self-contained: the agent resolves the download URL from its own
        // configured ServerUrl. No caller-supplied target is involved, so an empty target is
        // expected and valid. Checked before the not-empty guard below.
        if (kind == OperationKind.AgentSelfUpdate) return true;

        if (string.IsNullOrWhiteSpace(target)) return false;

        // Defense-in-depth upper bound: per-kind regexes already cap most targets tightly, but the
        // free-form kinds (pipeline JSON / version strings) were otherwise unbounded. 16 KiB is well
        // above any legitimate target (incl. artifact-collection JSON) yet rejects pathological input.
        if (target.Length > 16_384) return false;

        return kind switch
        {
            OperationKind.None => false,
            OperationKind.DockerRestartContainer or
            OperationKind.DockerStartContainer or
            OperationKind.DockerStopContainer or
            OperationKind.DockerPullImage => DockerTargetRegex().IsMatch(target),

            OperationKind.ServiceStart or
            OperationKind.ServiceStop or
            OperationKind.ServiceRestart or
            OperationKind.ServiceStatus or
            OperationKind.ServiceGetLogs or
            // Phase 3 (option B): same name format as the other service ops. The agent normalises a
            // trailing ".service"; the fixed-unit allow-list is enforced by the argv-exact sudoers
            // drop-in, not here - this only rejects malformed unit names.
            OperationKind.ServiceEnable => ServiceNameRegex().IsMatch(target),

            // S-FEAT-W8KN: install/uninstall is bounded to the closed apt-package allow-list (the
            // target is the resolved apt name, e.g. docker.io). Uninstall is further restricted to the
            // Removable subset so protected packages (databases, ufw/fail2ban, docker) can't be removed.
            // The agent re-checks here and the argv-exact sudoers drop-in is the OS-level backstop.
            OperationKind.ServiceInstall => ManageablePackages.IsManageablePackage(target),
            OperationKind.ServiceUninstall => ManageablePackages.IsRemovablePackage(target),

            // PLAN-006 4.1: whole-box `apt-get upgrade` - no per-package target (caller passes "-").
            // The dry-run/apply mode rides in AETHEUS_PATCH_DRY_RUN and the critical-package abort is
            // enforced agent-side; the argv-exact aetheus-patch sudoers drop-in is the OS backstop.
            OperationKind.SystemPackageUpgrade => true,

            // PLAN-006 4.2: firewall rule ops - target is the port (1-65535); protocol + source ride in
            // AETHEUS_FIREWALL_* env vars (re-validated agent- and helper-side). Enable/disable target is
            // a fixed verb. The anti-lockout guard (never deny the admin port) is enforced in the service.
            OperationKind.FirewallAllow or
            OperationKind.FirewallDeny or
            OperationKind.FirewallDeleteRule => int.TryParse(target, out var fwPort) && fwPort is >= 1 and <= 65535,
            OperationKind.FirewallSetEnabled => target is "enable" or "disable",

            OperationKind.ApacheReload or
            OperationKind.ApacheTestConfig or
            OperationKind.ApacheStart or
            OperationKind.ApacheStop or
            OperationKind.ApacheRestart => string.IsNullOrEmpty(target) || target == "-" || ServiceNameRegex().IsMatch(target),

            // Item #8: log type is a short closed enum; we accept "error" or "access" only, so a
            // caller can never trick the agent into reading an arbitrary file via the target.
            OperationKind.ApacheGetLogs => target is "error" or "access",

            // Item #5.3: vhost filename - strict regex blocks "..", "/", "\0" and similar
            // path-traversal vectors. The agent additionally pins the parent directory to
            // /etc/apache2/sites-available so even a regex bypass cannot reach outside.
            OperationKind.ApacheSaveConfig => ApacheSiteFileRegex().IsMatch(target),

            // Read a vhost config by site filename; read/write a .htaccess by absolute document root.
            OperationKind.ApacheGetConfig => ApacheSiteFileRegex().IsMatch(target),
            OperationKind.ApacheGetHtaccess or OperationKind.ApacheSaveHtaccess
                => !string.IsNullOrEmpty(target) && ApacheDocumentRootRegex().IsMatch(target),

            // type: apache-proxy - target is the vhost filename (same strict shape as SaveConfig). The
            // rendered ServerName/Upstream travel in AETHEUS_APACHE_* env vars; the agent pins the
            // parent dir to sites-available and re-validates before enabling + reloading.
            OperationKind.ApacheConfigureProxy => ApacheSiteFileRegex().IsMatch(target),

            // type: certbot - target is the primary domain to certify. Extra domains/email travel in
            // AETHEUS_CERTBOT_* env vars; the agent re-validates before the controlled-sudo certbot run.
            OperationKind.CertbotObtain => MailValidation.IsValidDomainName(target),

            // type: certbot management - renew/delete/revoke a named certificate. Target is the cert
            // lineage name; the root-owned certbot-manage helper re-validates the same shape before
            // running certbot --cert-name. Renew-all has no name (target unused), so anything is accepted.
            OperationKind.CertbotRenew or
            OperationKind.CertbotDelete or
            OperationKind.CertbotRevoke => CertbotCertNameRegex().IsMatch(target),
            OperationKind.CertbotRenewAll => true,

            // Items #10.2/#10.3 - target unused (caller passes "-" or empty). Anything
            // is accepted because the agent ignores it.
            OperationKind.RkhunterScan or
            OperationKind.RkhunterUpdate or
            OperationKind.RkhunterPropupd => true,

            // Mail service control - target is unused (caller passes "-"); anything accepted.
            OperationKind.MailStartPostfix or
            OperationKind.MailStopPostfix or
            OperationKind.MailRestartPostfix or
            OperationKind.MailReloadPostfix or
            OperationKind.MailStartDovecot or
            OperationKind.MailStopDovecot or
            OperationKind.MailRestartDovecot or
            OperationKind.MailReloadDovecot or
            OperationKind.MailFlushQueue or
            OperationKind.MailViewQueue or
            OperationKind.MailTestConfig => true,

            // Mail log reading - target is the log source: "postfix" or "dovecot".
            OperationKind.MailGetLogs => target is "postfix" or "dovecot",

            // S-FEAT-W8KN: mail setup - target is the mail domain. Hostname/selector/email/quota travel
            // in AETHEUS_MAIL_* env vars and the password via stdin; all are re-validated agent-side
            // by MailValidation before reaching the root-owned mail-setup helper.
            OperationKind.MailSetup => MailValidation.IsValidDomainName(target),

            // S-FEAT-W8KN incremental ops via the root-owned mail-manage helper. Each target is the
            // primary key for the op; the helper re-validates the same shape before touching anything.
            //  - add-domain / dkim-rotate: target is the domain.
            //  - add-account / add-alias:  target is the (account / source) email.
            // Auxiliary fields (parent domain, quota, destination, new selector) + the account password
            // travel in AETHEUS_MAIL_* env vars / stdin and are re-validated agent-side.
            OperationKind.MailAddDomain or
            OperationKind.MailDkimRotate => MailValidation.IsValidDomainName(target),
            OperationKind.MailAddAccount or
            OperationKind.MailAddAlias => MailValidation.IsValidEmail(target),

            // Removal / read ops via the same mail-manage helper (audit "dead shell action" migration):
            //  - remove-domain: target is the domain; dkim-read: target is the DKIM selector.
            //  - delete-account: target is the account email (parent domain in AETHEUS_MAIL_DOMAIN).
            //  - remove-alias: target is the alias source email.
            OperationKind.MailRemoveDomain => MailValidation.IsValidDomainName(target),
            OperationKind.MailDkimRead => MailValidation.IsValidDkimSelector(target),
            OperationKind.MailDeleteAccount or
            OperationKind.MailRemoveAlias => MailValidation.IsValidEmail(target),

            // Portsentry service control + status + logs - target unused.
            OperationKind.PortsentryStart or
            OperationKind.PortsentryStop or
            OperationKind.PortsentryRestart or
            OperationKind.PortsentryStatus or
            OperationKind.PortsentryGetLogs => true,

            // Portsentry unblock - target must be a valid IPv4 or IPv6 address.
            OperationKind.PortsentryUnblock => IPAddress.TryParse(target, out _),

            // Portsentry setup - target is the scan mode (alphanumeric); the TCP/UDP port lists travel in
            // AETHEUS_PORTSENTRY_*_PORTS env vars and are re-validated agent-side.
            OperationKind.PortsentrySetup => PortsentryValidation.IsValidMode(target),

            // Pipeline artifact collection / release / substitution - target is JSON or version string.
            OperationKind.PipelineCollectArtifacts or
            OperationKind.PipelineCreateRelease or
            OperationKind.PipelineSubstituteVariables or
            OperationKind.PipelineRestoreArtifacts => true,

            // Intentionally NOT listed (they fall through to `_ => false`): PipelinePublishCoverage/Lint/
            // Complexity. These are dispatched internally by the pipeline engine, never via the public
            // CreateOperation endpoint, so rejecting them there is correct. If a future endpoint can route
            // these kinds, add explicit cases here rather than relying on the default - otherwise the
            // default-false would silently reject (or, if flipped, silently accept) them.

            // Cross-agent deploy - target is the application instance name. The strict regex doubles as
            // the systemd template instance and the on-disk deploy-dir name, so it blocks unit-name and
            // path-traversal injection. Artifact/release/compose selectors travel in AETHEUS_DEPLOY_*
            // env vars and are re-validated agent-side before the root-owned restart helper runs.
            OperationKind.PipelineDeploy => DeployAppRegex().IsMatch(target),

            // S-TECH-87: target is a single TeamSpeak ServerQuery command line, built server-side from
            // typed DTOs and EscapeServerQuery-encoded. It is NOT shell - it travels to the TS3 query
            // port over TCP - so the only injection vector is a raw newline smuggling an extra
            // ServerQuery line. EscapeServerQuery already turns \n/\r into literal escapes, so we
            // reject any actual control character (incl. CR/LF) as a last-line defence; the 16 KiB cap
            // above bounds length (snapshot-deploy blobs are the largest legitimate payload).
            OperationKind.TeamspeakServerQuery => TeamspeakQueryRegex().IsMatch(target),

            // Full TeamSpeak install - target is the install path. Voice/query ports travel in
            // AETHEUS_TEAMSPEAK_* env vars and are re-validated agent-side before the root-owned
            // teamspeak-setup helper runs (the helper re-validates the path shape too).
            OperationKind.TeamspeakSetup => TeamspeakInstallPathRegex().IsMatch(target),

            // Graceful restart - target unused (warning seconds/message + query port travel in env).
            OperationKind.TeamspeakGracefulRestart => string.IsNullOrEmpty(target) || target == "-",
            // Log read - target is the install path (the agent tails {installPath}/logs/ directly).
            OperationKind.TeamspeakGetLogs => TeamspeakInstallPathRegex().IsMatch(target),

            // Phase 3: cron job id - also the cron.d filename suffix, so the strict id regex blocks
            // dots, slashes and path-traversal. User/schedule/command travel in env vars and are
            // re-validated agent-side via CronValidation before reaching the sudo helper.
            OperationKind.CronSave or
            OperationKind.CronDelete => CronValidation.IsValidIdentifier(target),

            _ => false
        };
    }
}
