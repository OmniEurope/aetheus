// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// A telemetry sub-tab that shows the web-analytics summary of one monitored app (Visitors, Top pages).
/// The tabs render only the active panel, so each view reads the summary when it is shown, and again
/// when the app changes.
/// </summary>
public abstract class AppWebAnalyticsSummaryViewBase : ComponentBase
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int AppId { get; set; }

    protected AppWebAnalyticsSummaryDto? _summary;
    protected bool _loading = true;
    protected bool _loadFailed;
    private int _lastAppId = -1;

    protected override async Task OnParametersSetAsync()
    {
        if (AppId == _lastAppId)
            return;
        _lastAppId = AppId;
        await LoadAsync();
    }

    protected Task RetryAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            _summary = await Api.Monitoring.GetAppWebAnalyticsAsync(AppId);
        }
        catch (HttpRequestException)
        {
            _summary = null;
            _loadFailed = true;
        }
        finally
        {
            _loading = false;
        }
    }
}
