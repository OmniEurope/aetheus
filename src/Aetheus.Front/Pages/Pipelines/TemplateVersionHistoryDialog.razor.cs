// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class TemplateVersionHistoryDialog : PipelineTemplateVersionHistoryBase
{
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public string TemplateName { get; set; } = string.Empty;
    [Parameter, EditorRequired] public int LatestVersion { get; set; }

    private RadzenDataGrid<PipelineTemplateVersionSummaryDto>? _versionsGrid;
    private int _initializedTemplateId;

    protected override async Task OnParametersSetAsync()
    {
        if (_initializedTemplateId == TemplateId) return;
        _initializedTemplateId = TemplateId;
        await InitializeComparisonAsync(LatestVersion);
    }

    private Task RetryVersionsAsync() => _versionsGrid?.Reload() ?? Task.CompletedTask;

    private void Close() => Dialog.Close();
}
