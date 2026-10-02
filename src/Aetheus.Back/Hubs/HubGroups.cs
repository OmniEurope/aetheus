// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Hubs;

/// <summary>
/// Centralized SignalR group name constants. Keep all group names here to avoid
/// magic-string drift across services and hubs.
/// </summary>
public static class HubGroups
{
    public const string AllServers = "all-servers";
    public const string Alerts = "alerts";
    public const string PipelineUpdates = "pipeline-updates";
    public const string AllReleases = "all-releases";
    public const string AllLogs = "all-logs";

    public static string Server(int id) => $"server-{id}";
    public static string PipelineRun(int runId) => $"pipeline-run-{runId}";
    public static string Release(int releaseId) => $"release-{releaseId}";

    /// <summary>Every run lifecycle notification targets the aggregate list, the owning pipeline,
    /// and the run-detail group. Keeping the set here prevents a status transition from refreshing
    /// list pages while leaving the open run page stale.</summary>
    public static string[] PipelineRunUpdates(int runId, int? pipelineId) => pipelineId is { } id
        ? [PipelineUpdates, Pipeline(id), PipelineRun(runId)]
        : [PipelineUpdates, PipelineRun(runId)];

    // F-05: per-resource fan-out groups (paired with the aggregate groups so Admins still receive
    // global broadcasts via the aggregate, while non-admin subscribers only get their resources).
    public static string Pipeline(int pipelineId) => $"pipeline-{pipelineId}";
    public static string ProjectReleases(int projectId) => $"project-releases-{projectId}";

    // F-05 CREATE blind-spot fix: per-organization aggregate groups for org-scoped resources
    // (Server, Project). A newly created resource's per-resource group is empty (nobody could have
    // joined it before it existed), so its Created/Registered broadcast only reached admins via the
    // aggregate group. Non-admin org members now also join the org group below, and lifecycle
    // broadcasts target it - org membership grants Read on every org resource, so this leaks nothing.
    public static string ServerOrg(int organizationId) => $"server-org-{organizationId}";
    public static string EntityOrg(Aetheus.Shared.Components.Auth.ResourceType type, int organizationId) => $"entity-{type}-org-{organizationId}";

    // Generic entity change groups, used by EntityHub to broadcast CRUD events for resources
    // that don't have their own dedicated hub (Project, Vault, VariableLibrary, ...).
    public static string EntityAll(Aetheus.Shared.Components.Auth.ResourceType type) => $"entity-{type}-all";
    public static string Entity(Aetheus.Shared.Components.Auth.ResourceType type, int id) => $"entity-{type}-{id}";
}
