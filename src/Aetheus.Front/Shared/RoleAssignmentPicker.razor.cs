// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public partial class RoleAssignmentPicker : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public List<string> SelectedRoles { get; set; } = [];
    [Parameter] public EventCallback<List<string>> SelectedRolesChanged { get; set; }
    [Parameter] public IReadOnlyList<string> InitialRoles { get; set; } = [];
    [Parameter] public bool Disabled { get; set; }

    private AetheusDataGrid<RoleDto>? _grid;
    private List<RoleDto> _roles = [];
    private int _totalCount;
    private int _page = 1;
    private int _pageSize = 25;
    private string? _sortBy = "Name";
    private bool _sortDescending;
    private bool _loading;
    private string? _search;
    private string? _columnSearch;
    private bool _assignedOnly;
    private CancellationTokenSource? _searchDebounce;

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        _pageSize = args.Top ?? 25;
        _page = ((args.Skip ?? 0) / _pageSize) + 1;
        (_sortBy, _sortDescending) = ResolveSort(args);
        _columnSearch = args.Filters?
            .FirstOrDefault(filter => string.Equals(
                filter.Property, nameof(RoleDto.Name), StringComparison.OrdinalIgnoreCase))
            ?.FilterValue?.ToString();
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        _loading = true;
        try
        {
            if (_assignedOnly || InitialRoles.Count > 0)
            {
                var source = _assignedOnly ? SelectedRoles : InitialRoles;
                var selected = source
                    .Where(role => MatchesSearch(role, _search)
                        && MatchesSearch(role, _columnSearch))
                    .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _totalCount = selected.Count;
                _roles = selected
                    .Skip((_page - 1) * _pageSize)
                    .Take(_pageSize)
                    .Select(role => new RoleDto { Name = role })
                    .ToList();
                return;
            }

            var result = await Api.Auth.GetRoleDtosAsync(
                _page, _pageSize, ActiveSearch, _sortBy, _sortDescending);
            _roles = result.Items;
            _totalCount = result.TotalCount;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task OnSearchInputAsync(ChangeEventArgs args)
    {
        _search = args.Value?.ToString();
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), _searchDebounce.Token);
            await InvokeAsync(ResetGridAsync);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke owns the pending search.
        }
    }

    private async Task ClearSearchAsync()
    {
        _searchDebounce?.Cancel();
        _search = null;
        await ResetGridAsync();
    }

    private async Task OnAssignedOnlyChanged(bool assignedOnly)
    {
        _assignedOnly = assignedOnly;
        await ResetGridAsync();
    }

    private async Task ResetGridAsync()
    {
        var alreadyOnFirstPage = _page == 1;
        _page = 1;
        if (_grid is not null)
        {
            if (alreadyOnFirstPage)
                await _grid.Reload();
            else
                await _grid.GoToPage(0);
        }
        else
            await LoadPageAsync();
    }

    private bool IsSelected(string role) =>
        SelectedRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    private string? ActiveSearch =>
        !string.IsNullOrWhiteSpace(_columnSearch) ? _columnSearch : _search;

    private static bool MatchesSearch(string value, string? search) =>
        string.IsNullOrWhiteSpace(search)
        || value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private async Task OnRoleToggledAsync(string role, bool selected)
    {
        var updated = SelectedRoles.ToList();
        var existing = updated.FindIndex(item =>
            string.Equals(item, role, StringComparison.OrdinalIgnoreCase));

        if (selected && existing < 0)
            updated.Add(role);
        else if (!selected && existing >= 0)
            updated.RemoveAt(existing);

        SelectedRoles = updated;
        await SelectedRolesChanged.InvokeAsync(updated);

        if (_assignedOnly)
            await LoadPageAsync();
    }

    private static (string? SortBy, bool Descending) ResolveSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy))
            return ("Name", false);

        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1
            && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    public ValueTask DisposeAsync()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = null;
        return ValueTask.CompletedTask;
    }
}
