// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Shared;

// PLAN-001: dialog form for creating/editing a monitored app. Closes with `true` on success so the
// caller reloads the list; `false`/dismiss leaves it untouched.
public partial class MonitoredAppFormDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int ProjectId { get; set; }

    /// <summary>The app being edited; <c>null</c> opens the dialog in create mode.</summary>
    [Parameter] public MonitoredAppDto? App { get; set; }

    private bool IsEdit => App is not null;
    private UpdateMonitoredAppRequest _model = new();
    private bool _busy;

    private List<ServerDto> _servers = [];
    private List<EnvironmentDto> _environments = [];

    protected override async Task OnInitializedAsync()
    {
        if (App is { } a)
        {
            _model = new UpdateMonitoredAppRequest
            {
                Name = a.Name,
                EnvironmentId = a.EnvironmentId,
                ServerId = a.ServerId,
                ProbeUrl = a.ProbeUrl,
                ProbeIntervalSeconds = a.ProbeIntervalSeconds,
                ProbeTimeoutSeconds = a.ProbeTimeoutSeconds,
                ExpectedStatusCode = a.ExpectedStatusCode,
                FailureThreshold = a.FailureThreshold,
                RecoveryThreshold = a.RecoveryThreshold,
                Enabled = a.Enabled
            };
        }

        try
        {
            var serversTask = Api.GetAllServersAsync();
            var environmentsTask = Api.GetAllEnvironmentsAsync(projectId: ProjectId);
            await Task.WhenAll(serversTask, environmentsTask);
            _servers = await serversTask;
            _environments = await environmentsTask;
        }
        catch (HttpRequestException)
        {
            // Dropdown lookups are best-effort; the form still works with free-form defaults.
        }
    }

    private async Task SubmitAsync()
    {
        if (string.IsNullOrWhiteSpace(_model.Name))
        {
            Toast.Warning("ValidationError", "RequiredFields");
            return;
        }

        _busy = true;
        try
        {
            if (IsEdit)
            {
                await Ui.RunAsync(
                    () => Api.UpdateMonitoredAppAsync(App!.Id, _model),
                    "MonitoredAppUpdated",
                    _ => { Dialog.Close(true); return Task.CompletedTask; },
                    successTitleKey: "Updated");
            }
            else
            {
                var create = new CreateMonitoredAppRequest
                {
                    Name = _model.Name,
                    ProjectId = ProjectId,
                    EnvironmentId = _model.EnvironmentId,
                    ServerId = _model.ServerId,
                    ProbeUrl = _model.ProbeUrl,
                    ProbeIntervalSeconds = _model.ProbeIntervalSeconds,
                    ProbeTimeoutSeconds = _model.ProbeTimeoutSeconds,
                    ExpectedStatusCode = _model.ExpectedStatusCode,
                    FailureThreshold = _model.FailureThreshold,
                    RecoveryThreshold = _model.RecoveryThreshold,
                    Enabled = _model.Enabled
                };
                await Ui.RunAsync(
                    () => Api.CreateMonitoredAppAsync(ProjectId, create),
                    "MonitoredAppCreated",
                    _ => { Dialog.Close(true); return Task.CompletedTask; },
                    successTitleKey: "Created");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void Cancel() => Dialog.Close(false);
}
