// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Shared;

public partial class AppIngestionSettings
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private IConfiguration Config { get; set; } = default!;

    [Parameter] public int AppId { get; set; }
    [Parameter] public MonitoredAppDto? App { get; set; }
    [Parameter] public bool CanWrite { get; set; }
    [Parameter] public EventCallback OnChanged { get; set; }

    private List<AppMetricThresholdDto> _thresholds = [];
    private string? _generatedKey;
    private bool _busy;
    private int _lastAppId = -1;
    private int _loadGeneration;

    private readonly CreateMetricThresholdRequest _newThreshold = new();
    private static readonly List<ComparisonOperator> _operators = Enum.GetValues<ComparisonOperator>().ToList();

    private string IngestEndpoint =>
        $"{(Config["ApiBaseUrl"] ?? LocalDevelopmentEndpoints.ApiHttpsBaseUrl).TrimEnd('/')}/api/ingest/otlp/v1";

    protected override async Task OnParametersSetAsync()
    {
        if (AppId == _lastAppId) return;
        _lastAppId = AppId;
        var generation = ++_loadGeneration;
        _generatedKey = null;
        await LoadThresholdsAsync(AppId, generation);
    }

    private Task LoadThresholdsAsync() => LoadThresholdsAsync(AppId, _loadGeneration);

    private async Task LoadThresholdsAsync(int appId, int generation)
    {
        try
        {
            var thresholds = await Api.GetAppThresholdsAsync(appId);
            if (generation == _loadGeneration && appId == AppId) _thresholds = thresholds;
        }
        catch (HttpRequestException)
        {
            if (generation == _loadGeneration && appId == AppId) _thresholds = [];
        }
    }

    private async Task GenerateKeyAsync()
    {
        _busy = true;
        try
        {
            var resp = await Api.GenerateIngestKeyAsync(AppId);
            if (resp is not null)
            {
                _generatedKey = resp.Key;
                Toast.Success("Created", "IngestKeyGenerated");
                await OnChanged.InvokeAsync();
            }
            else
            {
                Toast.Error("Error", "OperationFailed");
            }
        }
        finally { _busy = false; }
    }

    private async Task RevokeKeyAsync()
    {
        var confirmed = await Dialog.Confirm(L["RevokeIngestKeyConfirm"].Value, L["RevokeIngestKey"].Value,
            new ConfirmOptions { OkButtonText = L["Revoke"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var status = await Api.RevokeIngestKeyAsync(AppId);
        if (status.Success)
        {
            _generatedKey = null;
            Toast.Success("Deleted", "IngestKeyRevoked");
            await OnChanged.InvokeAsync();
        }
    }

    private async Task CopyKeyAsync()
    {
        if (_generatedKey is not null)
            await Clipboard.CopyAsync(_generatedKey);
    }

    private async Task AddThresholdAsync()
    {
        if (string.IsNullOrWhiteSpace(_newThreshold.MetricName))
        {
            Toast.Warning("ValidationError", "RequiredFields");
            return;
        }
        var created = await Api.CreateAppThresholdAsync(AppId, _newThreshold);
        if (created is not null)
        {
            _newThreshold.MetricName = string.Empty;
            _newThreshold.Threshold = 0;
            Toast.Success("Created", "ThresholdCreated");
            await LoadThresholdsAsync();
        }
    }

    private async Task DeleteThresholdAsync(int thresholdId)
    {
        var status = await Api.DeleteAppThresholdAsync(thresholdId);
        if (status.Success)
        {
            Toast.Success("Deleted", "ThresholdDeleted");
            await LoadThresholdsAsync();
        }
    }
}
