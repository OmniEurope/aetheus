// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Alerts;

// X4D8: dialog form for creating/editing an alert rule. Closes with `true` on success so the caller
// reloads the list; `false`/dismiss leaves the list untouched.
public partial class AlertEditDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    /// <summary>The rule being edited; <c>null</c> opens the dialog in create mode.</summary>
    [Parameter] public AlertRuleDto? Alert { get; set; }

    private bool IsEdit => Alert is not null;
    private UpdateAlertRuleRequest _model = new();
    private bool _busy;

    private List<object> _metricTypes = [];

    private List<object> _severityTypes = [];

    protected override void OnInitialized()
    {
        _metricTypes = Enum.GetValues<MetricType>()
            .Select(metric => (object)new { Text = L.Localize(metric), Value = metric })
            .ToList();
        _severityTypes = Enum.GetValues<AlertSeverity>()
            .Select(severity => (object)new { Text = L.Localize(severity), Value = severity })
            .ToList();
        if (Alert is { } a)
        {
            _model = new UpdateAlertRuleRequest
            {
                Name = a.Name,
                ServerId = a.ServerId,
                Metric = a.Metric,
                Operator = a.Operator,
                Threshold = a.Threshold,
                SustainedSeconds = a.SustainedSeconds,
                Severity = a.Severity,
                IsEnabled = a.IsEnabled,
                NotificationChannelId = a.NotificationChannelId
            };
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
                    () => Api.UpdateAlertRuleAsync(Alert!.Id, _model),
                    "AlertUpdated",
                    _ => { Dialog.Close(true); return Task.CompletedTask; },
                    successTitleKey: "Updated");
            }
            else
            {
                var create = new CreateAlertRuleRequest
                {
                    Name = _model.Name,
                    ServerId = _model.ServerId,
                    Metric = _model.Metric,
                    Operator = _model.Operator,
                    Threshold = _model.Threshold,
                    SustainedSeconds = _model.SustainedSeconds,
                    Severity = _model.Severity,
                    IsEnabled = _model.IsEnabled,
                    NotificationChannelId = _model.NotificationChannelId
                };
                await Ui.RunAsync(
                    () => Api.CreateAlertRuleAsync(create),
                    "AlertCreated",
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
