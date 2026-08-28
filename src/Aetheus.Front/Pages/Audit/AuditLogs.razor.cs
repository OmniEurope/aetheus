// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Audit;

public partial class AuditLogs : IDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private NotifyHelper Notify { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;

    private List<AuditLogDto> _logs = [];
    private int _count;
    private bool _loading;
    private string? _search;
    private string _actionFilter = string.Empty;
    private string _entityFilter = string.Empty;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private List<string> _actionOptions = [];
    private List<string> _entityOptions = [];
    private Timer? _debounceTimer;
    private const int DebounceMs = 300;
    private AuditChainVerificationResult? _chainResult;
    private bool _chainChecked;
    private bool _verifyingChain;

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }
        Breadcrumb.Set(new BreadcrumbItem(L["Administration"], "/admin"), new BreadcrumbItem(L["AuditLogs"]));

        try
        {
            var actionsTask = Api.Monitoring.GetAuditActionsAsync();
            var typesTask = Api.Monitoring.GetAuditEntityTypesAsync();
            await Task.WhenAll(actionsTask, typesTask);
            _actionOptions = await actionsTask;
            _entityOptions = await typesTask;
            // Stale-while-revalidate: pre-seed from the cached default view (grid PageSize = 50) so the
            // first paint is instant; LoadData then revalidates in the background.
            Cache.Seed<PaginatedResult<AuditLogDto>>(CacheKey(1, 50), ApplyLogs);
            await LoadData(new LoadDataArgs());
        }
        catch (HttpRequestException)
        {
            _actionOptions = [];
            _entityOptions = [];
        }
    }

    // The sort is part of the key: two orders sharing one entry means the second is served the first
    // one's rows, which is indistinguishable from a sort that does nothing.
    private string CacheKey(int page, int pageSize, string? sortBy = null, bool sortDescending = true) =>
        $"audit:{page}:{pageSize}:{_search}:{_actionFilter}:{_entityFilter}:{_dateFrom:o}:{_dateTo:o}:{sortBy}:{sortDescending}";

    private void ApplyLogs(PaginatedResult<AuditLogDto> result)
    {
        _logs = result.Items;
        _count = result.TotalCount;
    }

    private async Task LoadData(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest(50);
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(AuditLogDto.Timestamp), fallbackDescending: true);
        await Cache.RevalidateAsync(
            CacheKey(page, pageSize, sortBy, sortDescending),
            () => Api.Monitoring.GetAuditLogsAsync(page, pageSize, _search, _actionFilter, _entityFilter, null, _dateFrom, _dateTo,
                sortBy, sortDescending),
            ApplyLogs,
            loading => _loading = loading,
            () => InvokeAsync(StateHasChanged));
    }

    private async Task ReloadData()
    {
        _loading = true;
        var result = await Api.Monitoring.GetAuditLogsAsync(1, 50, _search, _actionFilter, _entityFilter, null, _dateFrom, _dateTo);
        _logs = result.Items;
        _count = result.TotalCount;
        _loading = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ClearFilters()
    {
        _search = null;
        _actionFilter = string.Empty;
        _entityFilter = string.Empty;
        _dateFrom = null;
        _dateTo = null;
        await ReloadData();
    }

    private async Task ShowDetailsDialogAsync(AuditLogDto log)
    {
        // Structured detail (actor / action / target / time) + server-verified chain-integrity badge.
        await Dialog.OpenAsync<AuditDetailDialog>(L["AuditEntryDetail"],
            new Dictionary<string, object?> { ["Log"] = log },
            new DialogOptions { Width = "700px", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });
    }

    private async Task VerifyChainAsync()
    {
        _verifyingChain = true;
        StateHasChanged();
        try
        {
            _chainResult = await Api.Monitoring.VerifyAuditChainAsync();
            if (_chainResult is not null)
            {
                Notify.Notify(
                    _chainResult.IsValid ? NotificationSeverity.Success : NotificationSeverity.Error,
                    "AuditVerifyChain",
                    _chainResult.IsValid ? L["AuditChainIntact"] : L["AuditChainBroken"]);
            }
            else
            {
                Notify.Error("Error", "OperationFailed");
            }
        }
        catch (HttpRequestException)
        {
            _chainResult = null; // honest "unavailable", never an optimistic green
            Notify.Error("Error", "OperationFailed");
        }
        finally
        {
            _chainChecked = true;
            _verifyingChain = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private Task CopyDetailsAsync(string? details) => Clipboard.CopyAsync(details, L["Details"]);

    private static BadgeStyle GetActionBadge(string action) => action switch
    {
        "Created" => BadgeStyle.Success,
        "Updated" => BadgeStyle.Info,
        "Deleted" => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };

    private void OnSearchChanged(string _)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(async _ =>
        {
            await InvokeAsync(ReloadData);
        }, null, DebounceMs, Timeout.Infinite);
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
    }
}
