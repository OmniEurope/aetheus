// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerLogsSection
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;

    [Parameter, EditorRequired] public int ServerId { get; set; }

    private PaginatedResult<TaskLogDto>? _result;
    private int? _loadedServerId;
    private int _page = 1;
    private bool _loading;
    private bool _loadMoreError;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedServerId == ServerId) return;
        _loadedServerId = ServerId;
        _page = 1;
        _loadMoreError = false;
        _loading = false;
        var serverId = ServerId;
        try
        {
            var result = await Api.Servers.GetServerLogsAsync(serverId, _page);
            if (ServerId == serverId) _result = result;
        }
        catch (HttpRequestException)
        {
            if (ServerId == serverId) _result = new PaginatedResult<TaskLogDto>();
        } // 401 on expired JWT - redirect handled by AuthProvider
    }

    private async Task LoadMoreAsync()
    {
        var serverId = ServerId;
        var nextPage = _page + 1;
        _loading = true;
        _loadMoreError = false;
        try
        {
            var next = await Api.Servers.GetServerLogsAsync(serverId, nextPage);
            if (ServerId != serverId) return;
            _page = nextPage;
            _result = _result! with
            {
                Items = [.. _result.Items, .. next.Items]
            };
        }
        catch (HttpRequestException)
        {
            if (ServerId == serverId) _loadMoreError = true;
        }
        finally
        {
            if (ServerId == serverId) _loading = false;
        }
    }

    private static BadgeStyle GetLevelBadge(TaskLogLevel level) => DisplayFormatting.TaskLogBadge(level);
}
