// SPDX-License-Identifier: EUPL-1.2
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Components.Shared;

public partial class MonitoredAppsStatusGrid
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    /// <summary>Null while the summary is loading.</summary>
    [Parameter] public IReadOnlyList<MonitoredAppStatusDto>? Applications { get; set; }
    [Parameter] public bool IsLoading { get; set; }

    /// <summary>The dashboard tile is narrow and leaves this column out; the Supervision page shows it.</summary>
    [Parameter] public bool ShowLastEvent { get; set; }

    private OmniDataGrid<MonitoredAppStatusDto>? _grid;
    private IReadOnlyList<MonitoredAppStatusDto>? _shownApplications;
    private static readonly TimeSpan NewRowHighlight = TimeSpan.FromSeconds(5);

    /// <summary>Recette R-210: the last-event column shows local time, so its range filter and sort
    /// read the same local instant.</summary>
    private static readonly Func<MonitoredAppStatusDto, object?> LocalLastEventAt = app => app.LastEventAt?.ToLocalTime();

    // Recette R-210: the status filter names the states as the badges do; built once so the column
    // sees the same delegate on every render.
    private Func<string, string>? _statusText;
    private Func<string, string> StatusText => _statusText ??= value =>
        Enum.TryParse<AppHealthStatus>(value, ignoreCase: true, out var status) ? L[$"AppHealth{status}"].Value : value;

    /// <summary>
    /// Recette R-227: the parent hands a new list on each live reload. The grid notes the rows it holds
    /// before it renders the new ones, so an application that appeared reads bold for a few seconds.
    /// The first list (from nothing) marks nothing.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(Applications, _shownApplications))
        {
            if (_shownApplications is not null && Applications is not null && _grid is not null)
                await _grid.RefreshAsync();
            _shownApplications = Applications;
        }
    }

    /// <summary>Recette R-467: the project's supervision page, opened on this application.</summary>
    private static string AppHref(MonitoredAppStatusDto app) => $"/projects/{app.ProjectId}/monitoring?app={app.Id}";

    internal static OmniTone HealthBadge(AppHealthStatus status) => status switch
    {
        AppHealthStatus.Up => OmniTone.Success,
        AppHealthStatus.Down => OmniTone.Danger,
        AppHealthStatus.Degraded => OmniTone.Warning,
        _ => OmniTone.Neutral
    };
}
