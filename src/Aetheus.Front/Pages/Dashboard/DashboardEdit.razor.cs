// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Helpers;
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Dashboard;

public partial class DashboardEdit
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private UiActions Ui { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    [Parameter] public int Id { get; set; }

    private DashboardDto? _dashboard;
    private int? _loadedId;
    private bool _authorized;
    private string _name = string.Empty;
    private bool _isDefault;
    private Guid _rowVersion;
    private List<WidgetModel> _widgets = [];
    private bool _loading = true;
    private bool _saving;
    private int? _dragIndex;

    private List<object> WidgetTypeOptions = [];

    protected override void OnInitialized()
    {
        WidgetTypeOptions = Enum.GetValues<DashboardWidgetType>()
            .Select(t => (object)new { Text = L.Localize(t), Value = t })
            .ToList();

        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }

        _authorized = true;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!_authorized || _loadedId == Id) return;
        _loadedId = Id;
        var id = Id;
        _loading = true;
        _dashboard = null;

        DashboardDto? dashboard;
        try { dashboard = await Api.GetDashboardAsync(id); }
        catch (HttpRequestException) { dashboard = null; }
        if (Id != id) return;
        _dashboard = dashboard;
        if (dashboard is null)
        {
            _loading = false;
            Nav.NavigateTo("/dashboards");
            return;
        }

        Breadcrumb.Set(
            new BreadcrumbItem(L["Dashboards"], "/dashboards"),
            new BreadcrumbItem(dashboard.Name));

        _name = dashboard.Name;
        _isDefault = dashboard.IsDefault;
        _rowVersion = dashboard.RowVersion;
        _widgets = dashboard.Widgets
            .OrderBy(w => w.Row).ThenBy(w => w.Column)
            .Select(w => new WidgetModel
            {
                WidgetType = w.WidgetType,
                Title = w.Title,
                Column = w.Column,
                Row = w.Row,
                Width = w.Width,
                Height = w.Height,
                ConfigurationJson = w.ConfigurationJson,
                IsVisible = w.IsVisible
            })
            .ToList();

        _loading = false;
    }

    private void AddWidget()
    {
        var nextRow = _widgets.Count > 0 ? _widgets.Max(w => w.Row) + 1 : 0;
        _widgets.Add(new WidgetModel
        {
            WidgetType = DashboardWidgetType.ServerCount,
            Title = $"{L["WidgetDefaultName"]} {_widgets.Count + 1}",
            Row = nextRow,
            Width = 1,
            Height = 1,
            IsVisible = true
        });
    }

    private void RemoveWidget(WidgetModel widget)
    {
        _widgets.Remove(widget);
    }

    private void OnDragStart(int index)
    {
        _dragIndex = index;
    }

    private void OnDrop(int targetIndex)
    {
        if (_dragIndex is null || _dragIndex == targetIndex) return;

        var dragged = _widgets[_dragIndex.Value];
        _widgets.RemoveAt(_dragIndex.Value);
        _widgets.Insert(targetIndex, dragged);
        _dragIndex = null;
        RecalculateRows();
    }

    private void OnDragEnd()
    {
        _dragIndex = null;
    }

    private void MoveUp(WidgetModel widget)
    {
        var index = _widgets.IndexOf(widget);
        if (index <= 0) return;
        (_widgets[index], _widgets[index - 1]) = (_widgets[index - 1], _widgets[index]);
        RecalculateRows();
    }

    private void MoveDown(WidgetModel widget)
    {
        var index = _widgets.IndexOf(widget);
        if (index < 0 || index >= _widgets.Count - 1) return;
        (_widgets[index], _widgets[index + 1]) = (_widgets[index + 1], _widgets[index]);
        RecalculateRows();
    }

    private void RecalculateRows()
    {
        for (var i = 0; i < _widgets.Count; i++)
            _widgets[i].Row = i;
    }

    private void ToggleConfig(WidgetModel widget)
    {
        widget.ShowConfig = !widget.ShowConfig;
    }

    private async Task SaveDashboard()
    {
        if (string.IsNullOrWhiteSpace(_name))
        {
            Toast.Warning("ValidationError", "RequiredFields");
            return;
        }

        _saving = true;
        var request = new UpdateDashboardRequest
        {
            Name = _name,
            IsDefault = _isDefault,
            RowVersion = _rowVersion,
            Widgets = _widgets.Select(w => new CreateDashboardWidgetRequest
            {
                WidgetType = w.WidgetType,
                Title = w.Title,
                Column = w.Column,
                Row = w.Row,
                Width = w.Width,
                Height = w.Height,
                ConfigurationJson = w.ConfigurationJson,
                IsVisible = w.IsVisible
            }).ToList()
        };

        await Ui.RunAsync(
            () => Api.UpdateDashboardAsync(Id, request),
            successKey: "DashboardUpdated",
            successTitleKey: "Updated",
            onSuccess: result =>
            {
                _dashboard = result;
                _rowVersion = result.RowVersion;
                return Task.CompletedTask;
            });
        _saving = false;
    }

    private static string GetWidgetIcon(DashboardWidgetType type) => type switch
    {
        DashboardWidgetType.ServerCount => "dns",
        DashboardWidgetType.PipelineActivity => "account_tree",
        DashboardWidgetType.RecentRuns => "history",
        DashboardWidgetType.ServerList => "list",
        DashboardWidgetType.TaskSummary => "task_alt",
        DashboardWidgetType.BuildSuccess => "check_circle",
        DashboardWidgetType.Custom => "widgets",
        _ => "widgets"
    };

    private sealed class WidgetModel
    {
        public DashboardWidgetType WidgetType { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Column { get; set; }
        public int Row { get; set; }
        public int Width { get; set; } = 1;
        public int Height { get; set; } = 1;
        public string? ConfigurationJson { get; set; }
        public bool IsVisible { get; set; } = true;
        public bool ShowConfig { get; set; }
    }
}
