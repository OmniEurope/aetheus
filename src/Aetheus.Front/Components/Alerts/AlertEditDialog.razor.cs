// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Alerts;

// X4D8: dialog form for creating/editing an alert rule. Closes with `true` on success so the caller
// reloads the list; `false`/dismiss leaves the list untouched.
public partial class AlertEditDialog : EntityEditDialogBase
{
    /// <summary>The rule being edited; <c>null</c> opens the dialog in create mode.</summary>
    [Parameter] public AlertRuleDto? Alert { get; set; }

    private bool IsEdit => Alert is not null;
    private UpdateAlertRuleRequest _model = new();
    private List<OmniOption<MetricType>> _metricTypes = [];

    private List<OmniOption<AlertSeverity>> _severityTypes = [];

    protected override void OnInitialized()
    {
        _metricTypes = Enum.GetValues<MetricType>()
            .Select(metric => new OmniOption<MetricType>(metric, L.Localize(metric)))
            .ToList();
        _severityTypes = Enum.GetValues<AlertSeverity>()
            .Select(severity => new OmniOption<AlertSeverity>(severity, L.Localize(severity)))
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
        if (!ValidateRequiredName(_model.Name))
            return;

        await RunBusyAsync(async () =>
        {
            if (IsEdit)
            {
                await Ui.RunAsync(
                    () => Api.Monitoring.UpdateAlertRuleAsync(Alert!.Id, _model),
                    "AlertUpdated",
                    _ => CloseAfterSuccessAsync(),
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
                    () => Api.Monitoring.CreateAlertRuleAsync(create),
                    "AlertCreated",
                    _ => CloseAfterSuccessAsync(),
                    successTitleKey: "Created");
            }
        });
    }
}
