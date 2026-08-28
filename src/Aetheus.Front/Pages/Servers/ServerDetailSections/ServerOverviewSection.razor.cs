// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerOverviewSection : IDisposable
{
    private string ScannerDiagnosticState(string value) => value.Contains(":degraded:", StringComparison.OrdinalIgnoreCase)
        || value.Contains(":unavailable:", StringComparison.OrdinalIgnoreCase)
        ? L["AnalysisScannerUnavailable"]
        : value.Contains(":available-on-demand:", StringComparison.OrdinalIgnoreCase)
            ? L["AnalysisScannerOnDemand"] : L["AnalysisScannerReady"];

    private static BadgeStyle ScannerDiagnosticStyle(string value) => value.Contains(":degraded:", StringComparison.OrdinalIgnoreCase)
        || value.Contains(":unavailable:", StringComparison.OrdinalIgnoreCase)
        ? BadgeStyle.Danger
        : value.Contains(":available-on-demand:", StringComparison.OrdinalIgnoreCase) ? BadgeStyle.Warning : BadgeStyle.Success;

    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter] public bool MetricsReceived { get; set; }
    [Parameter] public IReadOnlyList<ServerMetricDto> Metrics { get; set; } = [];
    // TC8R: toolchain capability lives on the heartbeat DTO (not ServerDetailDto); the loader surfaces
    // the latest value. Null = unknown (no heartbeat yet / legacy agent).
    [Parameter] public bool? PipelineRunnerAvailable { get; set; }
    [Parameter] public DateTime? LastUpdated { get; set; }
    [Parameter] public EventCallback<ServerDetailDto> ServerChanged { get; set; }

    private bool _runnerBusy;
    private bool _isolationBusy;

    private bool CanAdmin => Permissions.CanAdmin(ResourceType.Server, Server.Id);

    private long? BuildCacheGrowthBytes
    {
        get
        {
            var points = Metrics
                .Where(metric => metric.Timestamp != default && metric.BuildCacheAvailable)
                .OrderBy(metric => metric.Timestamp)
                .ToList();
            return points.Count < 2 ? null : points[^1].BuildCacheBytes - points[0].BuildCacheBytes;
        }
    }

    // MainLayout loads permissions in parallel with route activation, so this section can
    // render before Permissions.IsLoaded - re-render when they land so the admin-gated
    // runner/isolation toggles reactivate. (Canonical pattern: Projects.razor.cs.)
    protected override void OnInitialized() => Permissions.OnPermissionsChanged += OnPermissionsChanged;

    private void OnPermissionsChanged() => InvokeAsync(StateHasChanged);

    public void Dispose() => Permissions.OnPermissionsChanged -= OnPermissionsChanged;

    /// <summary>
    /// Flips PipelineRunnerEnabled. On enable, requires explicit confirmation (the operator is
    /// authorising arbitrary shell execution from any pipeline whose tag/pool resolves here).
    /// On disable, no confirmation - disabling is always safe.
    /// </summary>
    private async Task TogglePipelineRunnerAsync()
    {
        if (_runnerBusy) return;
        var newValue = !(Server.PipelineRunnerEnabled ?? false);

        if (newValue)
        {
            var confirmed = await Dialog.Confirm(
                string.Format(L["PipelineRunnerEnableConfirmBody"], Server.Name),
                L["PipelineRunnerEnableConfirmTitle"],
                new ConfirmOptions { OkButtonText = L["Enable"], CancelButtonText = L["Cancel"] });
            if (confirmed != true) return;
        }

        _runnerBusy = true;
        try
        {
            var updated = await Api.Servers.SetPipelineRunnerEnabledAsync(Server.Id, newValue);
            if (updated is null)
            {
                Toast.Error(L["ActionFailed"]);
                return;
            }

            // Reflect the new value locally; the SignalR `ServerUpdated` broadcast will also
            // refresh siblings, but this gives an immediate, optimistic UX.
            Server = Server with { PipelineRunnerEnabled = updated.PipelineRunnerEnabled };
            if (ServerChanged.HasDelegate)
                await ServerChanged.InvokeAsync(Server);

            Toast.Success(updated.PipelineRunnerEnabled == true
                ? L["PipelineRunnerEnabledToast"]
                : L["PipelineRunnerDisabledToast"]);
        }
        finally
        {
            _runnerBusy = false;
        }
    }

    /// <summary>
    /// Flips RequireContainerIsolation. On enforce, confirms (every future process-mode step on
    /// this runner will be blocked). On relax, no confirmation.
    /// </summary>
    private async Task ToggleContainerIsolationAsync()
    {
        if (_isolationBusy) return;
        var newValue = !Server.RequireContainerIsolation;

        if (newValue)
        {
            var confirmed = await Dialog.Confirm(
                string.Format(L["ContainerIsolationEnforceConfirmBody"], Server.Name),
                L["ContainerIsolationEnforceConfirmTitle"],
                new ConfirmOptions { OkButtonText = L["Enforce"], CancelButtonText = L["Cancel"] });
            if (confirmed != true) return;
        }

        _isolationBusy = true;
        try
        {
            var updated = await Api.Servers.SetContainerIsolationRequiredAsync(Server.Id, newValue);
            if (updated is null)
            {
                Toast.Error(L["ActionFailed"]);
                return;
            }

            Server = Server with { RequireContainerIsolation = updated.RequireContainerIsolation };
            if (ServerChanged.HasDelegate)
                await ServerChanged.InvokeAsync(Server);

            Toast.Success(updated.RequireContainerIsolation
                ? L["ContainerIsolationEnforcedToast"]
                : L["ContainerIsolationRelaxedToast"]);
        }
        finally
        {
            _isolationBusy = false;
        }
    }

    private static string FormatAgentVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "-";
        var parts = version.Split('.');
        return parts.Length >= 4
            ? $"v{string.Join(".", parts.Take(3))}"
            : $"v{version}";
    }

    private string FormatAgentVersionWithTarget()
    {
        var installed = FormatAgentVersion(Server.AgentVersion);
        if (Server.AgentCompatibility is not
            {
                Status: AgentCompatibilityStatus.UpdateRecommended or AgentCompatibilityStatus.UpdateRequired,
                TargetVersion: { Length: > 0 } target
            }
            || string.Equals(Server.AgentVersion, target, StringComparison.OrdinalIgnoreCase))
            return installed;

        return string.Format(L["AgentVersionUpdateAvailable"], installed, target);
    }

    private static string FormatBytes(long bytes)
    {
        const double gib = 1024d * 1024d * 1024d;
        const double mib = 1024d * 1024d;
        return bytes >= gib ? $"{bytes / gib:F1} GiB" : $"{bytes / mib:F0} MiB";
    }

    private static string FormatTrend(long? bytes) => bytes switch
    {
        null => "-",
        > 0 => $"+{FormatBytes(bytes.Value)}",
        < 0 => $"-{FormatBytes(Math.Abs(bytes.Value))}",
        _ => "0 MiB"
    };
}
