// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Shared;

/// <summary>
/// PLAN-001 phases 2-4: OTLP telemetry panel for a project. An app picker selects one monitored app,
/// then sub-tabs (Metrics / Logs / Errors / Ingestion) show its telemetry. RBAC inherited from the Project.
/// </summary>
public partial class AppTelemetryPanel : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;

    [Parameter] public int ProjectId { get; set; }

    private List<MonitoredAppDto> _apps = [];
    private int? _selectedAppId;
    private MonitoredAppDto? _selectedApp;
    private bool _canWrite;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();
        await LoadAppsAsync();
        _loading = false;
    }

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() => _canWrite = Permissions.CanWrite(ResourceType.Project);

    private async Task LoadAppsAsync()
    {
        try { _apps = await Api.GetMonitoredAppsAsync(ProjectId); }
        catch (HttpRequestException) { _apps = []; }

        if (_apps.Count > 0 && (_selectedAppId is null || _apps.All(a => a.Id != _selectedAppId)))
            _selectedAppId = _apps[0].Id;
        _selectedApp = _apps.FirstOrDefault(a => a.Id == _selectedAppId);
    }

    private void OnAppChanged() => _selectedApp = _apps.FirstOrDefault(a => a.Id == _selectedAppId);

    // The ingestion settings changed the app (key generated/revoked): reload so HasIngestKey/counters refresh.
    private async Task OnTelemetryChangedAsync()
    {
        await LoadAppsAsync();
        StateHasChanged();
    }

    public ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        return ValueTask.CompletedTask;
    }
}
