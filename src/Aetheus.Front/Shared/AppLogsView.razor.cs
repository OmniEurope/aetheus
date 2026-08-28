// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public partial class AppLogsView
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int AppId { get; set; }

    private sealed record SeverityOption(string Label, int? MinSeverity);

    private List<AppLogEntryDto> _logs = [];
    private int _total;
    private string? _search;
    private int? _minSeverity;
    private bool _loading;
    private int _lastAppId = -1;

    private readonly List<SeverityOption> _severities =
    [
        new("AllLevels", null), new("AppHealthUp", 9), new("Warning", 13), new("Error", 17)
    ];

    protected override async Task OnParametersSetAsync()
    {
        if (AppId == _lastAppId) return;
        _lastAppId = AppId;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var result = await Api.Monitoring.GetAppLogsAsync(AppId, hours: 24, minSeverity: _minSeverity, search: _search, page: 1, pageSize: 200);
            _logs = result.Items;
            _total = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            _logs = [];
            _total = 0;
        }
        _loading = false;
    }

    private Task OnFilterChanged() => LoadAsync();

    private static BadgeStyle SeverityBadge(int severityNumber) => severityNumber switch
    {
        >= 21 => BadgeStyle.Danger,   // FATAL
        >= 17 => BadgeStyle.Danger,   // ERROR
        >= 13 => BadgeStyle.Warning,  // WARN
        >= 9 => BadgeStyle.Info,      // INFO
        >= 5 => BadgeStyle.Light,     // DEBUG
        _ => BadgeStyle.Light     // TRACE / unspecified
    };

    private static string SeverityLabel(AppLogEntryDto e) =>
        !string.IsNullOrEmpty(e.SeverityText) ? e.SeverityText! : e.SeverityNumber switch
        {
            >= 21 => "FATAL",
            >= 17 => "ERROR",
            >= 13 => "WARN",
            >= 9 => "INFO",
            >= 5 => "DEBUG",
            >= 1 => "TRACE",
            _ => "-"
        };
}
