// SPDX-License-Identifier: EUPL-1.2
using Markdig;
using Microsoft.AspNetCore.WebUtilities;

namespace Aetheus.Front.Pages.Analysis;

public partial class AnalysisFindingDetail : ComponentBase, IDisposable
{
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().DisableHtml().Build();

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private NotificationService Notifications { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Parameter] public int Id { get; set; }

    private AnalysisFindingDto? _finding;
    private List<AnalysisFindingOccurrenceDto> _occurrences = [];
    private List<AnalysisFindingDecisionDto> _decisions = [];
    private bool _loading = true;
    private bool _saving;
    private bool _canAdmin;
    private bool _canWrite;
    private bool _creatingTicket;
    private int? _createdTicketId;
    private bool _showPromptPreview;
    private readonly DecisionFormModel _decision = new();
    private readonly AnalysisFindingStatus[] _decisionStatuses =
        [AnalysisFindingStatus.Accepted, AnalysisFindingStatus.FalsePositive, AnalysisFindingStatus.Mitigated];

    protected override async Task OnInitializedAsync()
    {
        Permissions.OnPermissionsChanged += PermissionsChanged;
        try { await LoadAsync(); }
        catch (HttpRequestException) { _finding = null; }
        _loading = false;
    }

    private async Task LoadAsync()
    {
        _finding = await Api.Analysis.GetAnalysisFindingAsync(Id);
        if (_finding is null) return;
        Breadcrumb.Set(
            new BreadcrumbItem(L["AnalysisPortfolio"], "/analysis"),
            new BreadcrumbItem(_finding.Title));
        var occurrences = Api.Analysis.GetAnalysisFindingOccurrencesAsync(Id);
        var decisions = Api.Analysis.GetAnalysisFindingDecisionsAsync(Id);
        await Task.WhenAll(occurrences, decisions);
        _occurrences = await occurrences;
        _decisions = await decisions;
        UpdatePermission();
    }

    private async Task SaveDecisionAsync()
    {
        if (_finding is null) return;
        if (_decision.ExpiresAt.HasValue && _decision.ExpiresAt.Value <= DateTime.Now)
        {
            Toast.Error("Error", "AnalysisDecisionExpirationFuture");
            return;
        }

        _saving = true;
        try
        {
            var outcome = await Api.Analysis.CreateAnalysisFindingDecisionAsync(Id, new CreateAnalysisFindingDecisionRequest
            {
                Status = _decision.Status,
                Reason = _decision.Reason,
                ExpiresAt = _decision.ExpiresAt
            });
            if (outcome.Value is not null)
            {
                Notifications.Notify(NotificationSeverity.Success, L["Saved"], L["AnalysisDecisionSaved"]);
                await LoadAsync();
                _decision.Reason = string.Empty;
                _decision.ExpiresAt = null;
            }
            else
            {
                Toast.Error("Error", "SaveFailed");
            }
        }
        catch (HttpRequestException)
        {
            Toast.Error("Error", "SaveFailed");
        }
        finally
        {
            _saving = false;
        }
    }

    private void OpenHelp()
    {
        if (_finding?.HelpUri is { } value
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps)
            Navigation.NavigateTo(uri.AbsoluteUri, forceLoad: true);
    }

    private string PromptMarkdown => _finding is null
        ? string.Empty
        : AnalysisAiPromptBuilder.BuildSingle(_finding, L);

    private string PromptHtml => Markdown.ToHtml(PromptMarkdown, MarkdownPipeline);
    private static string LocationRange(AnalysisFindingOccurrenceDto source) =>
        source.EndLine is { } endLine && source.StartLine is { } startLine && endLine > startLine
            ? $"{startLine}-{endLine}"
            : source.StartLine?.ToString() ?? string.Empty;

    private static bool HasNavigableFile(AnalysisFindingOccurrenceDto source)
    {
        var path = source.FilePath?.Trim();
        return source.RepositoryId.HasValue
            && !string.IsNullOrWhiteSpace(path)
            && path != "/"
            && !path.EndsWith('/');
    }

    private static string SourceFileHref(AnalysisFindingOccurrenceDto source)
    {
        var path = source.FilePath!.Trim().TrimStart('/');
        var fileRef = !string.IsNullOrWhiteSpace(source.CommitHash)
            ? source.CommitHash
            : source.BranchName;
        var query = new Dictionary<string, string?>
        {
            ["tab"] = "files",
            ["ref"] = fileRef,
            ["path"] = path,
            ["line"] = source.StartLine?.ToString()
        };
        return QueryHelpers.AddQueryString($"/git-repositories/{source.RepositoryId}", query);
    }

    private void SelectDecisionStatus(AnalysisFindingStatus status) => _decision.Status = status;

    private string DecisionStatusText(AnalysisFindingStatus status) => L[$"AnalysisFindingStatus{status}"];
    private string DecisionStatusDescription(AnalysisFindingStatus status) => L[$"AnalysisDecision{status}Description"];
    private static string DecisionStatusIcon(AnalysisFindingStatus status) => status switch
    {
        AnalysisFindingStatus.Accepted => "verified_user",
        AnalysisFindingStatus.FalsePositive => "fact_check",
        AnalysisFindingStatus.Mitigated => "health_and_safety",
        _ => "gavel"
    };

    private static ButtonStyle DecisionStatusStyle(AnalysisFindingStatus status) => status switch
    {
        AnalysisFindingStatus.Accepted => ButtonStyle.Warning,
        AnalysisFindingStatus.FalsePositive => ButtonStyle.Info,
        AnalysisFindingStatus.Mitigated => ButtonStyle.Success,
        _ => ButtonStyle.Light
    };

    private static BadgeStyle DecisionStatusBadgeStyle(AnalysisFindingStatus status) => status switch
    {
        AnalysisFindingStatus.Accepted => BadgeStyle.Warning,
        AnalysisFindingStatus.FalsePositive => BadgeStyle.Info,
        AnalysisFindingStatus.Mitigated => BadgeStyle.Success,
        _ => BadgeStyle.Light
    };

    private void TogglePromptPreview() => _showPromptPreview = !_showPromptPreview;
    private Task CopyPromptAsync() => Clipboard.CopyAsync(PromptMarkdown, L["AnalysisPromptCopied"]);
    private Task DownloadPromptAsync() => Js.InvokeVoidAsync(
        "downloadFile", $"finding-{Id}-ai-prompt.md", PromptMarkdown, "text/markdown").AsTask();

    private async Task CreateTicketAsync()
    {
        if (_finding is null || !_canWrite || _creatingTicket || _createdTicketId.HasValue) return;
        _creatingTicket = true;
        var workItem = await Api.Pipelines.CreateWorkItemAsync(new CreateWorkItemRequest
        {
            ProjectId = _finding.ProjectId,
            Type = WorkItemType.Bug,
            Title = $"[{_finding.Severity}] {_finding.Title}",
            Description = $"Analysis finding #{_finding.Id}\n\n{_finding.Message}\n\nRule: {_finding.RuleId}\nFingerprint: {_finding.Fingerprint}",
            Status = WorkItemStatus.New,
            Priority = (int)_finding.Severity,
            Tags = ["analysis", _finding.Category.ToString().ToLowerInvariant()],
            ExternalId = $"analysis:{_finding.Id}",
            ExternalUrl = Navigation.Uri
        });
        if (workItem is not null)
        {
            _createdTicketId = workItem.Id;
            Notifications.Notify(NotificationSeverity.Success, L["Created"], L["AnalysisTicketCreated"]);
        }
        _creatingTicket = false;
    }

    private void PermissionsChanged()
    {
        UpdatePermission();
        _ = InvokeAsync(StateHasChanged);
    }

    private void UpdatePermission()
    {
        _canAdmin = _finding is not null && Permissions.CanAdmin(ResourceType.Project, _finding.ProjectId);
        _canWrite = _finding is not null && Permissions.CanWrite(ResourceType.Project, _finding.ProjectId);
    }

    private static BadgeStyle SeverityStyle(AnalysisSeverity severity) => severity switch
    {
        AnalysisSeverity.Critical => BadgeStyle.Danger,
        AnalysisSeverity.High => BadgeStyle.Warning,
        AnalysisSeverity.Medium => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };

    public void Dispose() => Permissions.OnPermissionsChanged -= PermissionsChanged;

    private sealed class DecisionFormModel
    {
        public AnalysisFindingStatus Status { get; set; } = AnalysisFindingStatus.Accepted;
        [Required, StringLength(2000, MinimumLength = 10)] public string Reason { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
    }
}
