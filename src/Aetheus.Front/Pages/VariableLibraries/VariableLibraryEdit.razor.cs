// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Aetheus.Front.Layout;
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Pages.VariableLibraries;

public partial class VariableLibraryEdit
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private ProjectNavContextService ProjectNav { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;

    [Parameter] public int? Id { get; set; }

    [SupplyParameterFromQuery] public int? ProjectId { get; set; }
    [SupplyParameterFromQuery] public int? EnvironmentId { get; set; }
    [SupplyParameterFromQuery] public int? ProjectServerId { get; set; }

    private VariableLibraryDetailDto? _detail;
    private LibraryModel _model = new();
    private List<ProjectDto> _projects = [];
    private bool _isNew => Id is null or 0;
    private bool _saving;
    private bool _loading = true;
    private bool _loadFailed;
    private (int? Id, int? ProjectId, int? EnvironmentId, int? ProjectServerId)? _previousKey;
    private int _loadGeneration;

    private NewEntryModel _newEntry = new();
    private string _editEntryKey = string.Empty;
    private string _editEntryValue = string.Empty;
    private VariableEntryDto? _editingEntry;
    private RadzenDataGrid<VariableEntryDto>? _entriesGrid;
    private List<VariableEntryDto> _entries = [];
    private int _entryCount;
    private bool _entriesLoading;
    private bool _entriesLoadFailed;
    private int _entryPage = 1;
    private int _entryPageSize = 25;
    private string _entrySortBy = "Key";
    private bool _entrySortDescending;

    protected override async Task OnParametersSetAsync()
    {
        var key = (Id, ProjectId, EnvironmentId, ProjectServerId);
        if (key == _previousKey) return;
        _previousKey = key;
        var generation = ++_loadGeneration;
        var isNew = key.Id is null or 0;

        _detail = null;
        _model = new LibraryModel();
        ResetEntryBuffers();
        _entries = [];
        _entryCount = 0;
        _entriesLoadFailed = false;
        _entryPage = 1;
        _entryPageSize = 25;
        _entrySortBy = "Key";
        _entrySortDescending = false;
        _loading = true;
        _loadFailed = false;

        try
        {
            var projects = await Api.GetAllProjectsAsync();
            VariableLibraryDetailDto? detail = null;

            if (!isNew)
            {
                detail = await Api.GetVariableLibraryDetailAsync(key.Id!.Value);
            }
            if (generation != _loadGeneration || key != (Id, ProjectId, EnvironmentId, ProjectServerId)) return;

            _projects = projects;
            _detail = detail;
            if (detail is not null)
            {
                _entries = detail.Entries;
                _entryCount = detail.EntryCount;
                _model = new LibraryModel
                {
                    Name = detail.Name,
                    Description = detail.Description,
                    ProjectId = detail.ProjectId,
                    EnvironmentId = detail.EnvironmentId,
                    ProjectServerId = detail.ProjectServerId
                };
            }
            else if (isNew)
            {
                if (key.ProjectId is > 0 && projects.Any(p => p.Id == key.ProjectId.Value))
                    _model.ProjectId = key.ProjectId;
                else if (key.EnvironmentId is > 0)
                    _model.EnvironmentId = key.EnvironmentId;
                else if (key.ProjectServerId is > 0)
                    _model.ProjectServerId = key.ProjectServerId;
            }
        }
        catch (HttpRequestException)
        {
            if (generation == _loadGeneration)
                _loadFailed = true;
        }
        finally
        {
            if (generation == _loadGeneration)
                _loading = false;
        }

        // Publish the parent project so the NavMenu keeps the project's submenu open
        // while we're editing one of its variable libraries.
        ProjectNav.Set(_model.ProjectId);

        Breadcrumb.Set(
            new BreadcrumbItem(L["VariableLibraries"], "/variable-libraries"),
            new BreadcrumbItem(isNew ? L["NewVariableLibrary"] : _detail?.Name ?? L["VariableLibrary"]));
    }

    private async Task OnSubmit()
    {
        _saving = true;
        try
        {
            if (_isNew)
            {
                var created = await Api.CreateVariableLibraryAsync(new CreateVariableLibraryRequest
                {
                    Name = _model.Name,
                    Description = _model.Description,
                    ProjectId = _model.ProjectId,
                    EnvironmentId = _model.EnvironmentId,
                    ProjectServerId = _model.ProjectServerId
                });
                if (created is not null)
                {
                    Toast.Success("Created", "VariableLibraryCreated");
                    Nav.NavigateTo($"/variable-libraries/{created.Id}");
                }
            }
            else
            {
                var updated = await Api.UpdateVariableLibraryAsync(Id!.Value, new UpdateVariableLibraryRequest
                {
                    Name = _model.Name,
                    Description = _model.Description,
                    ProjectId = _model.ProjectId,
                    EnvironmentId = _model.EnvironmentId,
                    ProjectServerId = _model.ProjectServerId,
                    RowVersion = _detail?.RowVersion ?? Guid.Empty
                });
                if (updated is not null)
                {
                    Toast.Success("Saved", "VariableLibrarySaved");
                    await ReloadDetail();
                }
            }
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task RetryLoadAsync()
    {
        _previousKey = null;
        await OnParametersSetAsync();
    }

    private async Task OnDelete()
    {
        var confirmed = await Dialog.Confirm(L["DeleteVariableLibraryConfirm"].Value, L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var status = await Api.DeleteVariableLibraryAsync(Id!.Value);
        if (!status.Success)
        {
            Toast.Error("Error", "OperationFailed");
            return;
        }
        Cache.InvalidatePrefix("variable-libraries:"); // S-TECH-SWIV
        Nav.NavigateTo("/variable-libraries");
    }

    private async Task AddEntry(NewEntryModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Key))
            return;

        var entry = await Api.CreateVariableEntryAsync(Id!.Value, new CreateVariableEntryRequest
        {
            Key = model.Key,
            Value = model.Value
        });
        if (entry is not null)
        {
            _newEntry = new NewEntryModel();
            Toast.Success("Added", "EntryAdded");
            await ReloadEntriesAsync();
        }
    }

    private void EditEntry(VariableEntryDto entry)
    {
        _editingEntry = entry;
        _editEntryKey = entry.Key;
        _editEntryValue = entry.Value;
        _entriesGrid?.EditRow(entry);
    }

    private void OnEditableCellKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e, VariableEntryDto entry)
    {
        if (e.Key is "Enter" or " " or "Spacebar")
            EditEntry(entry);
    }

    private void OnEntryKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e, VariableEntryDto entry)
    {
        if (e.Key == "Enter")
            _entriesGrid?.UpdateRow(entry);
        else if (e.Key == "Escape")
            _entriesGrid?.CancelEditRow(entry);
    }

    private async Task OnEntryUpdate(VariableEntryDto entry)
    {
        var updated = await Api.UpdateVariableEntryAsync(Id!.Value, entry.Id, new UpdateVariableEntryRequest
        {
            Key = _editEntryKey,
            Value = _editEntryValue
        });
        if (updated is null)
        {
            Toast.Error("Error", "OperationFailed");
            return;
        }
        _editingEntry = null;
        Toast.Success("Saved", "EntryUpdated");
        await ReloadEntriesAsync();
    }

    private async Task DeleteEntry(int entryId)
    {
        var confirmed = await Dialog.Confirm(L["DeleteEntryConfirm"].Value, L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var status = await Api.DeleteVariableEntryAsync(Id!.Value, entryId);
        if (!status.Success)
        {
            Toast.Error("Error", "OperationFailed");
            return;
        }
        Toast.Success("Deleted", "EntryDeleted");
        await ReloadEntriesAsync();
    }

    private void ResetEntryBuffers()
    {
        if (_editingEntry is not null)
            _entriesGrid?.CancelEditRow(_editingEntry);
        _editingEntry = null;
        _newEntry = new NewEntryModel();
        _editEntryKey = string.Empty;
        _editEntryValue = string.Empty;
    }

    private async Task ShowVersions(VariableEntryDto entry)
    {
        await Dialog.OpenAsync<VersionHistoryDialog>(
            string.Format(L["VersionHistory"], entry.Key),
            new Dictionary<string, object?>
            {
                { "LibraryId", Id!.Value },
                { "EntryId", entry.Id }
            },
            new DialogOptions { Width = "600px" });
    }

    private async Task ExportEntries()
    {
        var entries = await Api.ExportVariableEntriesAsync(Id!.Value);
        var json = JsonSerializer.Serialize(entries.Select(e => new { e.Key, e.Value }), new JsonSerializerOptions { WriteIndented = true });
        await JS.InvokeVoidAsync("downloadFile", $"{_detail?.Name ?? "entries"}.json", json, "application/json");
        Toast.Success("Exported", "EntriesExported", entries.Count);
    }

    private async Task ImportEntries()
    {
        var json = await Dialog.OpenAsync<ImportJsonDialog>(
            L["ImportEntries"].Value,
            new Dictionary<string, object?>(),
            new DialogOptions { Width = "500px" });

        if (json is not string content || string.IsNullOrWhiteSpace(content)) return;

        List<CreateVariableEntryRequest>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<CreateVariableEntryRequest>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            Toast.Error("Error", "InvalidJsonFormat");
            return;
        }

        if (entries is null or { Count: 0 }) return;

        var result = await Api.ImportVariableEntriesAsync(Id!.Value, entries);
        if (result is not null)
        {
            Toast.Success("Imported", "EntriesImported", result.ImportedCount);
            await ReloadEntriesAsync();
        }
    }

    private async Task ReloadDetail()
    {
        _detail = await Api.GetVariableLibraryDetailAsync(Id!.Value);
        _entryCount = _detail?.EntryCount ?? 0;
    }

    private async Task LoadEntriesAsync(LoadDataArgs args)
    {
        if (Id is null or 0) return;
        _entryPageSize = args.Top ?? 25;
        _entryPage = ((args.Skip ?? 0) / _entryPageSize) + 1;
        (_entrySortBy, _entrySortDescending) = ResolveEntrySort(args);
        await LoadEntryPageAsync();
    }

    private async Task LoadEntryPageAsync()
    {
        if (Id is null or 0) return;
        _entriesLoading = true;
        _entriesLoadFailed = false;
        try
        {
            var result = await Api.GetVariableEntriesPageAsync(
                Id.Value, _entryPage, _entryPageSize,
                sortBy: _entrySortBy, sortDescending: _entrySortDescending);
            _entries = result.Items;
            _entryCount = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            _entriesLoadFailed = true;
        }
        finally
        {
            _entriesLoading = false;
        }
    }

    private async Task ReloadEntriesAsync()
    {
        await ReloadDetail();
        await LoadEntryPageAsync();
    }

    private Task RetryEntriesAsync()
        => _entriesGrid?.Reload() ?? Task.CompletedTask;

    private static (string SortBy, bool Descending) ResolveEntrySort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("Key", false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class NewEntryModel
    {
        [Required, StringLength(200)]
        public string Key { get; set; } = string.Empty;

        [StringLength(10_000)]
        public string Value { get; set; } = string.Empty;
    }

    private class LibraryModel
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        public int? ProjectId { get; set; }
        public int? EnvironmentId { get; set; }
        public int? ProjectServerId { get; set; }
    }
}
