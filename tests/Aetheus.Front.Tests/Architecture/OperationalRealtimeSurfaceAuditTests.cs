// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Architecture;

/// <summary>
/// Guards the pages whose displayed state can change without a local user action. Static editors
/// and historical detail views are intentionally excluded; operational lists and dashboards must
/// retain their SignalR/event subscription and reconnect path, or their explicit bounded refresh
/// loop when the underlying source is stream-only.
/// </summary>
public sealed class OperationalRealtimeSurfaceAuditTests
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredContracts =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Pages/Dashboard/Home.razor.cs"] =
                ["ServerHeartbeat", "PipelineRunStarted", "EntityChanged", "RejoinOnReconnect"],
            ["Pages/Servers/Servers.razor.cs"] =
                ["ServerHeartbeat", "ServerOffline", "RejoinOnReconnect"],
            ["Services/ServerDetailLoader.cs"] =
                ["Heartbeat", "TaskCompleted", "RejoinOnReconnect"],
            ["Shared/PipelinesList.razor.cs"] =
                ["PipelineRunStarted", "PipelineRunCompleted", "EntityChanged", "RejoinOnReconnect"],
            ["Pages/Pipelines/PipelineRunLiveConnection.cs"] =
                ["PipelineRunCompleted", "StepCompleted", "RejoinOnReconnect"],
            ["Pages/Tasks/TaskListView.razor.cs"] =
                ["TaskQueued", "TaskCompleted", "RejoinOnReconnect"],
            ["Pages/Alerts/Alerts.razor.cs"] =
                ["AlertRuleChanged", "RejoinOnReconnect"],
            ["Shared/ReleasesList.razor.cs"] =
                ["ReleaseCreated", "ReleaseStatusChanged", "RejoinOnReconnect"],
            ["Pages/Git/GitRepositoryRealtimeSubscription.cs"] =
                ["BranchesChanged", "CommitsChanged", "RejoinOnReconnect"],
            ["Shared/MonitoredAppsList.razor.cs"] =
                ["EntityChanged", "ResourceType.Project", "RejoinOnReconnect"],
            ["Pages/Analysis/AnalysisPortfolio.razor.cs"] =
                ["EntityChanged", "ResourceType.Project", "RejoinOnReconnect"],
            ["Pages/ServiceConnections/ServiceConnections.razor.cs"] =
                ["EntityChanged", "ResourceType.ServiceConnection", "RejoinOnReconnect"],
            ["Pages/Pipelines/PipelineTemplates.razor.cs"] =
                ["EntityChanged", "ResourceType.PipelineTemplate", "RejoinOnReconnect"],
            ["Pages/Ai/AiTasks.razor.cs"] =
                ["TaskTracker.OnTaskCompleted", "OperationKind.AiRun", "FindRunResultAsync"],
            ["Pages/Backups/Backups.razor.cs"] =
                ["OperationalRealtimeEvents.BackupChanged", "RejoinOnReconnect", "TrailingReloadCoalescer"],
            ["Shared/RealtimeAdminGridPageBase.cs"] =
                ["AdminEntitySubscription", "TrailingReloadCoalescer"],
            ["Pages/PackageFeeds/PackageFeedsAdmin.razor.cs"] =
                ["RealtimeAdminGridPageBase", "AdminEntities.PackageFeed"],
            ["Pages/PackageFeeds/PackageFeedPackagesDialog.razor.cs"] =
                ["AdminEntitySubscription", "AdminEntities.PackageFeed", "TrailingReloadCoalescer"],
            ["Pages/PackageRegistry/PackageRegistryAdmin.razor.cs"] =
                ["RealtimeAdminGridPageBase", "AdminEntities.PackageRegistry"],
            ["Pages/PackageRegistry/PackageRegistryPackageDialog.razor.cs"] =
                ["AdminEntitySubscription", "AdminEntities.PackageRegistry", "TrailingReloadCoalescer"],
            ["Pages/Projects/Projects.razor.cs"] =
                ["EntityChanged", "PipelineRunStarted", "ResourceType.Project", "RejoinOnReconnect"],
            ["Pages/Projects/ProjectDetailSections/ProjectOverviewSection.razor.cs"] =
                ["PipelineRunStarted", "PipelineRunCompleted", "RejoinOnReconnect"],
            ["Pages/Releases/ReleaseDetail.razor.cs"] =
                ["ReleaseStatusChanged", "RejoinOnReconnect"],
            ["Pages/Logs/Logs.razor.cs"] =
                ["LogReceived", "LogsReceived", "RejoinOnReconnect"],
            ["Pages/Logs/SystemLogs.razor.cs"] =
                ["PeriodicTimer", "AutoRefreshIntervalMs"],
            ["Shared/EnvironmentsList.razor.cs"] =
                ["EntityChanged", "ResourceType.Environment", "RejoinOnReconnect"],
            ["Shared/VariableLibrariesList.razor.cs"] =
                ["EntityChanged", "ResourceType.VariableLibrary", "RejoinOnReconnect"],
            ["Shared/VaultsList.razor.cs"] =
                ["EntityChanged", "ResourceType.Vault", "RejoinOnReconnect"]
        };

    [Fact]
    public void OperationalSurfaces_KeepTheirRealtimeContracts()
    {
        var front = Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");
        var missing = new List<string>();

        foreach (var (relativePath, contracts) in RequiredContracts)
        {
            var path = Path.Combine(front, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                missing.Add($"{relativePath}: file missing");
                continue;
            }

            var source = File.ReadAllText(path);
            missing.AddRange(contracts
                .Where(contract => !source.Contains(contract, StringComparison.Ordinal))
                .Select(contract => $"{relativePath}: missing {contract}"));
        }

        Assert.True(RequiredContracts.Count >= 27, "Realtime inventory unexpectedly shrank.");
        Assert.Empty(missing);
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
