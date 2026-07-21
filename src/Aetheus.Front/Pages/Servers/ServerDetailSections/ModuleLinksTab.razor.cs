// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ModuleLinksTab
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public int ServerId { get; set; }
    [Parameter, EditorRequired] public ModuleLinkType SourceType { get; set; }
    [Parameter, EditorRequired] public List<string> Resources { get; set; } = [];

    private List<LinkedResourceDto> _links = [];
    private AetheusDataGrid<LinkedResourceDto>? _grid;
    private int _totalCount;
    private int _page = 1;
    private int _pageSize = 25;
    private string _sortBy = "Identifier";
    private bool _sortDescending;
    private string? _search;
    private bool _loading = true;
    private bool _detecting;

    private ModuleLinkType[] _targetTypes = [];

    protected override async Task OnParametersSetAsync()
    {
        _targetTypes = Enum.GetValues<ModuleLinkType>().Where(t => t != SourceType).ToArray();
        _page = 1;
        await LoadLinksAsync();
    }

    private async Task LoadLinksAsync()
    {
        _loading = true;
        try
        {
            var resourceIdentifiers = Resources
                .Where(resource => !string.IsNullOrWhiteSpace(resource))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (resourceIdentifiers.Count == 0)
            {
                _links = [];
                _totalCount = 0;
                return;
            }

            var result = await Api.GetModuleLinksPageAsync(ServerId, new ModuleLinkPageRequest
            {
                SourceType = SourceType,
                ResourceIdentifiers = resourceIdentifiers,
                Page = _page,
                PageSize = _pageSize,
                Search = _search,
                SortBy = _sortBy,
                SortDescending = _sortDescending
            });
            _links = result.Items;
            _totalCount = result.TotalCount;
        }
        catch
        {
            Toast.Error(L["LoadFailed"]);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task OnLoadDataAsync(LoadDataArgs args)
    {
        (_page, _pageSize) = args.ToPageRequest();
        (_sortBy, _sortDescending) = GetSort(args);
        await LoadLinksAsync();
    }

    private async Task OnSearchChangedAsync(object _)
    {
        if (_grid is not null && _page != 1)
        {
            _page = 1;
            await _grid.GoToPage(0);
            return;
        }

        _page = 1;
        await LoadLinksAsync();
    }

    private async Task AutoDetectAsync()
    {
        _detecting = true;
        try
        {
            var success = await Api.AutoDetectModuleLinksAsync(ServerId);
            if (success)
            {
                Toast.Success(L["AutoDetectComplete"]);
                await LoadLinksAsync();
            }
            else
            {
                Toast.Error(L["ActionFailed"]);
            }
        }
        catch
        {
            Toast.Error(L["ActionFailed"]);
        }
        finally
        {
            _detecting = false;
        }
    }

    private async Task OpenAddDialogAsync()
    {
        var dialogResult = await Dialog.OpenAsync<AddModuleLinkDialog>(
            L["AddLink"].Value,
            new Dictionary<string, object?>
            {
                { "TargetTypes", _targetTypes },
                { "Resources", Resources }
            },
            new DialogOptions { Width = "400px" });

        if (dialogResult is AddModuleLinkResult result)
        {
            await AddLinkAsync(result);
        }
    }

    private async Task AddLinkAsync(AddModuleLinkResult request)
    {
        try
        {
            var result = await Api.CreateModuleLinkAsync(ServerId, new CreateModuleLinkRequest
            {
                SourceType = SourceType,
                SourceIdentifier = request.SourceIdentifier,
                TargetType = request.TargetType,
                TargetIdentifier = request.TargetIdentifier
            });
            if (result is not null)
            {
                Toast.Success(L["LinkCreated"]);
                await LoadLinksAsync();
            }
            else
            {
                Toast.Error(L["ActionFailed"]);
            }
        }
        catch
        {
            Toast.Error(L["ActionFailed"]);
        }
    }

    private async Task DeleteLinkAsync(int linkId)
    {
        var success = await Api.DeleteModuleLinkAsync(ServerId, linkId);
        if (success)
        {
            Toast.Success(L["LinkDeleted"]);
            await LoadLinksAsync();
        }
        else
        {
            Toast.Error(L["ActionFailed"]);
        }
    }

    internal static BadgeStyle GetTypeBadge(ModuleLinkType type) => type switch
    {
        ModuleLinkType.Docker => BadgeStyle.Primary,
        ModuleLinkType.Apache => BadgeStyle.Warning,
        ModuleLinkType.Certbot => BadgeStyle.Success,
        _ => BadgeStyle.Light
    };

    private static (string SortBy, bool Descending) GetSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("Identifier", false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1
            && string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase));
    }
}
