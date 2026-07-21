// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Shared;

public partial class AppErrorsView
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int AppId { get; set; }

    private List<AppErrorEventDto> _errors = [];
    private int _total;
    private bool _loading;
    private int _lastAppId = -1;
    private int _page = 1;
    private const int PageSize = 25;

    protected override async Task OnParametersSetAsync()
    {
        if (AppId == _lastAppId) return;
        _lastAppId = AppId;
        _page = 1;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var result = await Api.GetAppErrorsAsync(AppId, page: _page, pageSize: PageSize);
            _errors = result.Items;
            _total = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            _errors = [];
            _total = 0;
        }
        _loading = false;
    }

    private async Task OnPageChanged(PagerEventArgs args)
    {
        _page = args.Skip / PageSize + 1;
        await LoadAsync();
    }
}
