// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Enums;

namespace Aetheus.Shared.Constants;

/// <summary>Stable identifiers published by agents and consumed by assignment guards.</summary>
public static class AgentCapabilities
{
    public const string ShellExecution = "shell.execute";
    public const string DockerExecution = "docker.execute";
    public const string PipelineBuild = "pipeline.build";
    public const string ArtifactCollect = "artifact.collect";
    public const string ArtifactRestore = "artifact.restore";
    public const string Deployment = "deployment.apply";
    public const string SelfUpdate = "agent.self-update";
    public const string ServiceManagement = "service.manage";
    public const string PackageManagement = "package.manage";
    public const string PatchManagement = "patch.manage";
    public const string FirewallManagement = "firewall.manage";
    public const string ApacheManagement = "apache.manage";
    public const string CertbotManagement = "certbot.manage";
    public const string MailManagement = "mail.manage";
    public const string TeamspeakManagement = "teamspeak.manage";
    public const string PortsentryManagement = "portsentry.manage";
    public const string CronManagement = "cron.manage";
    public const string Analysis = "analysis.run";
    public const string AiExecution = "ai.run";
    public const string Backup = "backup.manage";

    public static IReadOnlyList<string> SoftwareCapabilities { get; } =
    [
        ShellExecution,
        PipelineBuild,
        ArtifactCollect,
        ArtifactRestore,
        SelfUpdate,
        Analysis,
        AiExecution
    ];

    public static string? RequiredFor(OperationKind operation) =>
        RequiredPipelineCapability(operation)
        ?? RequiredInfrastructureCapability(operation)
        ?? RequiredServiceCapability(operation);

    private static string? RequiredPipelineCapability(OperationKind operation) => operation switch
    {
        OperationKind.None => ShellExecution,
        OperationKind.AiRun => AiExecution,
        OperationKind.AgentSelfUpdate => SelfUpdate,
        OperationKind.PipelineCollectArtifacts => ArtifactCollect,
        OperationKind.PipelineRestoreArtifacts => ArtifactRestore,
        OperationKind.PipelineDeploy => Deployment,
        // Smoke only probes an already-deployed origin and reports findings, so it needs the same
        // capability as the other evidence-producing pipeline steps, not the deployment grant.
        OperationKind.PipelineSmoke => PipelineBuild,
        // The blue-green operations mutate a live environment, so they sit behind the deployment
        // grant alongside PipelineDeploy rather than the evidence capability.
        >= OperationKind.BlueGreenMigrate and <= OperationKind.BlueGreenRetire => Deployment,
        >= OperationKind.PipelineCreateRelease and <= OperationKind.PipelinePublishObservabilityBundle => PipelineBuild,
        >= OperationKind.BackupExecute and <= OperationKind.BackupRestore => Backup,
        _ => null
    };

    private static string? RequiredInfrastructureCapability(OperationKind operation) => operation switch
    {
        >= OperationKind.DockerRestartContainer and <= OperationKind.DockerPullImage => DockerExecution,
        OperationKind.SystemPackageUpgrade => PatchManagement,
        >= OperationKind.ApacheReload and <= OperationKind.ApacheApplyConfigSet => ApacheManagement,
        >= OperationKind.CertbotObtain and <= OperationKind.CertbotRevoke => CertbotManagement,
        >= OperationKind.RkhunterScan and <= OperationKind.RkhunterPropupd => Analysis,
        >= OperationKind.FirewallAllow and <= OperationKind.FirewallSetEnabled => FirewallManagement,
        _ => null
    };

    private static string? RequiredServiceCapability(OperationKind operation) => operation switch
    {
        >= OperationKind.ServiceStart and <= OperationKind.ServiceGetLogs => ServiceManagement,
        OperationKind.ServiceInstall or OperationKind.ServiceUninstall => PackageManagement,
        >= OperationKind.MailStartPostfix and <= OperationKind.MailDkimRead => MailManagement,
        >= OperationKind.PortsentryStart and <= OperationKind.PortsentrySetup => PortsentryManagement,
        >= OperationKind.CronSave and <= OperationKind.CronDelete => CronManagement,
        >= OperationKind.TeamspeakServerQuery and <= OperationKind.TeamspeakGetLogs => TeamspeakManagement,
        _ => null
    };
}
