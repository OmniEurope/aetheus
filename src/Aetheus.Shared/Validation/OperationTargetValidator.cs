// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Text.RegularExpressions;
using Aetheus.Shared.Constants;
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.Validation;

/// <summary>
/// F-32 / Hardening: single source of truth for per-<see cref="OperationKind"/> target validation.
/// Used by both the backend controller and the agent dispatch as a last-line defence.
/// </summary>
public static partial class OperationTargetValidator
{
    private static readonly HashSet<OperationKind> s_uncheckedTargets =
    [
        OperationKind.SystemPackageUpgrade,
        OperationKind.CertbotRenewAll,
        OperationKind.RkhunterScan,
        OperationKind.RkhunterUpdate,
        OperationKind.RkhunterPropupd,
        OperationKind.MailStartPostfix,
        OperationKind.MailStopPostfix,
        OperationKind.MailRestartPostfix,
        OperationKind.MailReloadPostfix,
        OperationKind.MailStartDovecot,
        OperationKind.MailStopDovecot,
        OperationKind.MailRestartDovecot,
        OperationKind.MailReloadDovecot,
        OperationKind.MailFlushQueue,
        OperationKind.MailViewQueue,
        OperationKind.MailTestConfig,
        OperationKind.PortsentryStart,
        OperationKind.PortsentryStop,
        OperationKind.PortsentryRestart,
        OperationKind.PortsentryStatus,
        OperationKind.PortsentryGetLogs,
        OperationKind.PipelineCollectArtifacts,
        OperationKind.PipelineCreateRelease,
        OperationKind.PipelineSubstituteVariables,
        OperationKind.PipelineRestoreArtifacts
    ];

    private static readonly HashSet<OperationKind> s_dockerTargets =
    [
        OperationKind.DockerRestartContainer,
        OperationKind.DockerStartContainer,
        OperationKind.DockerStopContainer,
        OperationKind.DockerPullImage
    ];

    private static readonly HashSet<OperationKind> s_serviceTargets =
    [
        OperationKind.ServiceStart,
        OperationKind.ServiceStop,
        OperationKind.ServiceRestart,
        OperationKind.ServiceStatus,
        OperationKind.ServiceGetLogs,
        OperationKind.ServiceEnable
    ];

    private static readonly HashSet<OperationKind> s_firewallPortTargets =
    [
        OperationKind.FirewallAllow,
        OperationKind.FirewallDeny,
        OperationKind.FirewallDeleteRule
    ];

    private static readonly HashSet<OperationKind> s_apacheServiceTargets =
    [
        OperationKind.ApacheReload,
        OperationKind.ApacheTestConfig,
        OperationKind.ApacheStart,
        OperationKind.ApacheStop,
        OperationKind.ApacheRestart
    ];

    private static readonly HashSet<OperationKind> s_apacheSiteTargets =
    [
        OperationKind.ApacheSaveConfig,
        OperationKind.ApacheGetConfig,
        OperationKind.ApacheConfigureProxy
    ];

    private static readonly HashSet<OperationKind> s_certificateTargets =
    [
        OperationKind.CertbotRenew,
        OperationKind.CertbotDelete,
        OperationKind.CertbotRevoke
    ];

    private static readonly HashSet<OperationKind> s_mailDomainTargets =
    [
        OperationKind.MailSetup,
        OperationKind.MailAddDomain,
        OperationKind.MailDkimRotate,
        OperationKind.MailRemoveDomain
    ];

    private static readonly HashSet<OperationKind> s_mailEmailTargets =
    [
        OperationKind.MailAddAccount,
        OperationKind.MailAddAlias,
        OperationKind.MailDeleteAccount,
        OperationKind.MailRemoveAlias
    ];

    private static readonly HashSet<OperationKind> s_teamspeakPathTargets =
    [
        OperationKind.TeamspeakSetup,
        OperationKind.TeamspeakGetLogs
    ];

    private static readonly HashSet<OperationKind> s_cronTargets =
    [
        OperationKind.CronSave,
        OperationKind.CronDelete
    ];

    // Every blue-green operation is scoped by the Compose project it acts on. Keeping the project
    // the operation target means the agent can refuse a task whose scope it does not recognise
    // before it touches any live environment.
    private static readonly HashSet<OperationKind> s_blueGreenTargets =
    [
        OperationKind.BlueGreenMigrate,
        OperationKind.BlueGreenUp,
        OperationKind.BlueGreenSwitch,
        OperationKind.BlueGreenCommit,
        OperationKind.BlueGreenRollback,
        OperationKind.BlueGreenRetire
    ];

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-:/]{0,127}$")]
    public static partial Regex DockerTargetRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-@]{0,63}$")]
    public static partial Regex ServiceNameRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_.\-]{0,127}\.conf$")]
    public static partial Regex ApacheSiteFileRegex();

    [GeneratedRegex(@"^/(?!.*\.\.)[a-zA-Z0-9._/-]{1,255}$")]
    public static partial Regex ApacheDocumentRootRegex();

    [GeneratedRegex(@"^[^\x00-\x1f]+$")]
    public static partial Regex TeamspeakQueryRegex();

    [GeneratedRegex(@"^/(?!.*\.\.)[a-zA-Z0-9._/-]{1,127}$")]
    public static partial Regex TeamspeakInstallPathRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9_-]{1,64}$")]
    public static partial Regex DeployAppRegex();

    // Smoke target: the http(s) origin being probed, with no path, query or credentials. Keeping the
    // target an origin rather than a full URL means the executor composes every probe path itself,
    // so a step definition cannot point the probe at an arbitrary endpoint.
    [GeneratedRegex(@"^https?://[a-zA-Z0-9.-]{1,253}(:[0-9]{1,5})?$")]
    public static partial Regex SmokeOriginRegex();

    // Docker Compose project name, matching Compose's own accepted charset.
    [GeneratedRegex(@"^[a-z0-9][a-z0-9_-]{0,62}$")]
    public static partial Regex ComposeProjectRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,199}$")]
    public static partial Regex CertbotCertNameRegex();

    public static bool IsValid(OperationKind kind, string? target)
    {
        if (kind == OperationKind.AgentSelfUpdate)
            return true;
        if (string.IsNullOrWhiteSpace(target) || target.Length > 16_384)
            return false;

        return ValidateSystemTarget(kind, target)
            ?? ValidateWebTarget(kind, target)
            ?? ValidateMailTarget(kind, target)
            ?? ValidatePipelineTarget(kind, target)
            ?? false;
    }

    private static bool? ValidateSystemTarget(OperationKind kind, string target)
    {
        if (kind == OperationKind.None)
            return false;
        if (s_uncheckedTargets.Contains(kind))
            return true;
        if (s_dockerTargets.Contains(kind))
            return DockerTargetRegex().IsMatch(target);
        if (s_serviceTargets.Contains(kind))
            return ServiceNameRegex().IsMatch(target);
        if (kind == OperationKind.ServiceInstall)
            return ManageablePackages.IsManageablePackage(target);
        if (kind == OperationKind.ServiceUninstall)
            return ManageablePackages.IsRemovablePackage(target);
        if (s_firewallPortTargets.Contains(kind))
            return int.TryParse(target, out var port) && port is >= 1 and <= 65535;
        if (kind == OperationKind.FirewallSetEnabled)
            return target is "enable" or "disable";
        return null;
    }

    private static bool? ValidateWebTarget(OperationKind kind, string target)
    {
        if (s_apacheServiceTargets.Contains(kind))
            return target == "-" || ServiceNameRegex().IsMatch(target);
        if (kind == OperationKind.ApacheGetLogs)
            return target is "error" or "access";
        if (s_apacheSiteTargets.Contains(kind))
            return ApacheSiteFileRegex().IsMatch(target);
        if (kind is OperationKind.ApacheGetHtaccess or OperationKind.ApacheSaveHtaccess)
            return ApacheDocumentRootRegex().IsMatch(target);
        if (kind == OperationKind.ApacheApplyConfigSet)
            return target == "config-set";
        if (kind == OperationKind.CertbotObtain)
            return MailValidation.IsValidDomainName(target);
        if (s_certificateTargets.Contains(kind))
            return CertbotCertNameRegex().IsMatch(target);
        return null;
    }

    private static bool? ValidateMailTarget(OperationKind kind, string target)
    {
        if (kind == OperationKind.MailGetLogs)
            return target is "postfix" or "dovecot";
        if (s_mailDomainTargets.Contains(kind))
            return MailValidation.IsValidDomainName(target);
        if (s_mailEmailTargets.Contains(kind))
            return MailValidation.IsValidEmail(target);
        if (kind == OperationKind.MailDkimRead)
            return MailValidation.IsValidDkimSelector(target);
        if (kind == OperationKind.PortsentryUnblock)
            return IPAddress.TryParse(target, out _);
        if (kind == OperationKind.PortsentrySetup)
            return PortsentryValidation.IsValidMode(target);
        return null;
    }

    private static bool? ValidatePipelineTarget(OperationKind kind, string target)
    {
        if (kind == OperationKind.AiRun)
            return target == "ai-run";
        if (kind == OperationKind.PipelineDeploy)
            return DeployAppRegex().IsMatch(target);
        if (kind == OperationKind.PipelineSmoke)
            return SmokeOriginRegex().IsMatch(target);
        if (s_blueGreenTargets.Contains(kind))
            return ComposeProjectRegex().IsMatch(target);
        if (kind == OperationKind.TeamspeakServerQuery)
            return TeamspeakQueryRegex().IsMatch(target);
        if (s_teamspeakPathTargets.Contains(kind))
            return TeamspeakInstallPathRegex().IsMatch(target);
        if (kind == OperationKind.TeamspeakGracefulRestart)
            return target == "-";
        if (s_cronTargets.Contains(kind))
            return CronValidation.IsValidIdentifier(target);
        return null;
    }
}
