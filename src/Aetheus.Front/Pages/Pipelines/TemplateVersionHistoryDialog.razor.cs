// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Pages.Pipelines;

public partial class TemplateVersionHistoryDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;

    [Parameter, EditorRequired] public int TemplateId { get; set; }
    [Parameter, EditorRequired] public string TemplateName { get; set; } = string.Empty;
    [Parameter, EditorRequired] public int LatestVersion { get; set; }

    private RadzenDataGrid<PipelineTemplateVersionSummaryDto>? _versionsGrid;
    private List<PipelineTemplateVersionSummaryDto> _versions = [];
    private int _versionCount;
    private bool _versionsLoading;
    private bool _versionsLoadFailed;
    private int _originalVersion;
    private int _modifiedVersion;
    private string _originalYaml = string.Empty;
    private string _modifiedYaml = string.Empty;
    private bool _yamlLoading;
    private bool _yamlLoadFailed;
    private int _yamlGeneration;
    private int _initializedTemplateId;

    protected override async Task OnParametersSetAsync()
    {
        if (_initializedTemplateId == TemplateId) return;
        _initializedTemplateId = TemplateId;
        _originalVersion = Math.Max(1, LatestVersion - 1);
        _modifiedVersion = Math.Max(1, LatestVersion);
        if (LatestVersion > 1)
            await LoadSelectedYamlAsync();
    }

    private async Task LoadVersionsAsync(LoadDataArgs args)
    {
        var pageSize = args.Top ?? 10;
        var page = ((args.Skip ?? 0) / pageSize) + 1;
        var (sortBy, descending) = ResolveSort(args);
        _versionsLoading = true;
        _versionsLoadFailed = false;
        try
        {
            var result = await Api.GetPipelineTemplateVersionsAsync(
                TemplateId, page, pageSize, sortBy, descending);
            _versions = result.Items;
            _versionCount = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            _versionsLoadFailed = true;
        }
        finally
        {
            _versionsLoading = false;
        }
    }

    private Task RetryVersionsAsync() => _versionsGrid?.Reload() ?? Task.CompletedTask;

    private async Task OnOriginalVersionChanged(int version)
    {
        _originalVersion = Math.Clamp(version, 1, LatestVersion);
        await LoadSelectedYamlAsync();
    }

    private async Task OnModifiedVersionChanged(int version)
    {
        _modifiedVersion = Math.Clamp(version, 1, LatestVersion);
        await LoadSelectedYamlAsync();
    }

    private async Task LoadSelectedYamlAsync()
    {
        var generation = ++_yamlGeneration;
        _yamlLoading = true;
        _yamlLoadFailed = false;
        try
        {
            var originalTask = Api.GetPipelineTemplateVersionAsync(TemplateId, _originalVersion);
            var modifiedTask = _modifiedVersion == _originalVersion
                ? originalTask
                : Api.GetPipelineTemplateVersionAsync(TemplateId, _modifiedVersion);
            await Task.WhenAll(originalTask, modifiedTask);
            if (generation != _yamlGeneration) return;
            var original = await originalTask;
            var modified = await modifiedTask;
            if (original is null || modified is null)
            {
                _yamlLoadFailed = true;
                return;
            }
            _originalYaml = original.YamlContent;
            _modifiedYaml = modified.YamlContent;
        }
        catch (HttpRequestException)
        {
            if (generation == _yamlGeneration)
                _yamlLoadFailed = true;
        }
        finally
        {
            if (generation == _yamlGeneration)
                _yamlLoading = false;
        }
    }

    private static (string SortBy, bool Descending) ResolveSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("Version", true);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private void Close() => Dialog.Close();
}
