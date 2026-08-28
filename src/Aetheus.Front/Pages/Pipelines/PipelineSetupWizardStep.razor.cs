// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineSetupWizardStep
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public int Step { get; set; }
    [Parameter] public IReadOnlyList<ProjectDto> Projects { get; set; } = [];
    [Parameter] public int? ProjectId { get; set; }
    [Parameter] public EventCallback<int?> ProjectIdChanged { get; set; }
    [Parameter] public string Prefix { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> PrefixChanged { get; set; }
    [Parameter] public bool Frontend { get; set; }
    [Parameter] public EventCallback<bool> FrontendChanged { get; set; }
    [Parameter] public bool Backend { get; set; }
    [Parameter] public EventCallback<bool> BackendChanged { get; set; }
    [Parameter] public bool Database { get; set; }
    [Parameter] public EventCallback<bool> DatabaseChanged { get; set; }
    [Parameter] public bool Ci { get; set; }
    [Parameter] public EventCallback<bool> CiChanged { get; set; }
    [Parameter] public bool Quality { get; set; }
    [Parameter] public EventCallback<bool> QualityChanged { get; set; }
    [Parameter] public bool Security { get; set; }
    [Parameter] public EventCallback<bool> SecurityChanged { get; set; }
    [Parameter] public bool Qa { get; set; }
    [Parameter] public EventCallback<bool> QaChanged { get; set; }
    [Parameter] public bool Candidate { get; set; }
    [Parameter] public EventCallback<bool> CandidateChanged { get; set; }
    [Parameter] public bool Nightly { get; set; }
    [Parameter] public EventCallback<bool> NightlyChanged { get; set; }
    [Parameter] public bool Production { get; set; }
    [Parameter] public EventCallback<bool> ProductionChanged { get; set; }
    [Parameter] public bool FastDeploy { get; set; }
    [Parameter] public EventCallback<bool> FastDeployChanged { get; set; }
    [Parameter] public bool Rollback { get; set; }
    [Parameter] public EventCallback<bool> RollbackChanged { get; set; }
    [Parameter] public bool Packages { get; set; }
    [Parameter] public EventCallback<bool> PackagesChanged { get; set; }
    [Parameter] public string ProjectName { get; set; } = string.Empty;
    [Parameter] public string ArchitectureSummary { get; set; } = string.Empty;
    [Parameter] public IReadOnlyList<PipelineSetupSummaryItem> SummaryItems { get; set; } = [];

    private static RenderFragment Option(
        string icon,
        string title,
        string description,
        bool value,
        EventCallback<bool> changed) => builder =>
    {
        builder.OpenElement(0, "label");
        builder.AddAttribute(1, "class", "pipeline-setup-option");
        builder.OpenComponent<RadzenCheckBox<bool>>(2);
        builder.AddAttribute(3, "Value", value);
        builder.AddAttribute(4, "Change", changed);
        builder.CloseComponent();
        builder.OpenComponent<RadzenIcon>(5);
        builder.AddAttribute(6, "Icon", icon);
        builder.CloseComponent();
        builder.OpenElement(7, "span");
        builder.OpenElement(8, "strong");
        builder.AddContent(9, title);
        builder.CloseElement();
        builder.OpenElement(10, "small");
        builder.AddContent(11, description);
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();
    };
}

public sealed record PipelineSetupSummaryItem(string Name, string TemplateName);
