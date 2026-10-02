// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class PipelineTemplateHistory : PipelineTemplateVersionHistoryBase
{
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;

    private AetheusDataGrid<PipelineTemplateVersionSummaryDto>? _versionsGrid;
    private string _templateName = string.Empty;
    private int _latestVersion;
    private bool _templateLoading = true;
    private bool _templateLoadFailed;
    private int _loadedTemplateId;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedTemplateId == TemplateId) return;
        _loadedTemplateId = TemplateId;
        _templateLoading = true;
        _templateLoadFailed = false;
        try
        {
            var template = await Api.PipelineTemplates.GetPipelineTemplateAsync(TemplateId);
            if (template is null) { _templateLoadFailed = true; return; }
            _templateName = template.Name;
            _latestVersion = template.Version;
            Breadcrumb.Set(new BreadcrumbItem(L["PipelineTemplates"], "/templates"),
                new BreadcrumbItem(template.Name, $"/templates/{TemplateId}"),
                new BreadcrumbItem(L["VersionHistoryTitle"]));
            await InitializeComparisonAsync(_latestVersion);
        }
        catch (HttpRequestException) { _templateLoadFailed = true; }
        finally { _templateLoading = false; }
    }

}
