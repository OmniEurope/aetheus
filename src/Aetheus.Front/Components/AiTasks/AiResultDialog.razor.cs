// SPDX-License-Identifier: EUPL-1.2
using Markdig;

namespace Aetheus.Front.Components.AiTasks;

public partial class AiResultDialog
{
    private const int PageSize = 25;
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().DisableHtml().Build();

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private NotifyHelper Notify { get; set; } = default!;

    [Parameter] public int? DefinitionId { get; set; }
    [Parameter] public AiRunResultDto? InitialResult { get; set; }

    private List<ResultRow> _results = [];
    private AiRunResultDto? _selected;
    private int _selectedId;
    private string _reportHtml = string.Empty;
    private bool _loading;
    private bool _applying;
    private int _totalCount;
    private int _page = 1;

    protected override async Task OnInitializedAsync()
    {
        _loading = true;
        try
        {
            await LoadPageAsync();
        }
        catch (HttpRequestException)
        {
            _results = [];
            Notify.Error("Error", "LoadFailed");
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadPageAsync()
    {
        List<AiRunResultDto> items;
        if (InitialResult is not null)
        {
            items = [InitialResult];
            _totalCount = 1;
        }
        else
        {
            var result = await Api.Ai.GetAiResultsAsync(
                page: _page, pageSize: PageSize, definitionId: DefinitionId);
            items = result.Items;
            _totalCount = result.TotalCount;
        }
        _results = items.Select(item => new ResultRow(
            item.Id, $"{item.CreatedAt.ToLocalTime():g} - {item.Verdict}", item)).ToList();
        if (_results.FirstOrDefault() is { } first)
        {
            _selectedId = first.Id;
            SelectResult();
        }
        else
        {
            _selected = null;
            _reportHtml = string.Empty;
        }
    }

    internal async Task OnPageChangedAsync(int page)
    {
        _page = page;
        await LoadPageAsync();
    }

    private void SelectResult()
    {
        _selected = _results.FirstOrDefault(item => item.Id == _selectedId)?.Result;
        _reportHtml = Markdown.ToHtml(_selected?.ReportMarkdown ?? string.Empty, MarkdownPipeline);
    }

    private static OmniTone VerdictStyle(AiRunResultDto result) => result.Verdict switch
    {
        AiVerdict.Pass => OmniTone.Success,
        AiVerdict.Fail => OmniTone.Danger,
        _ => result.Succeeded ? OmniTone.Accent : OmniTone.Warning
    };

    private static string FormatDuration(long durationMs) =>
        TimeSpan.FromMilliseconds(durationMs).ToString(@"mm\:ss");

    private void Close() => Dialog.Close();

    private Task DownloadPatchAsync() =>
        _selected?.DiffPatch is { } patch
            ? Js.InvokeVoidAsync(
                "downloadFile", $"ai-proposed-{_selected.Id}.patch", patch, "text/x-diff").AsTask()
            : Task.CompletedTask;

    private async Task ApplyPatchAsync()
    {
        if (_selected is null) return;
        _applying = true;
        try
        {
            var applied = await Api.Ai.ApplyAiPatchAsync(_selected.Id);
            if (applied is null) return;
            _selected = _selected with
            {
                ProposedRepositoryId = applied.RepositoryId,
                ProposedBranchName = applied.BranchName,
                ProposedCommitSha = applied.CommitSha
            };
            Notify.Success("Created", "ProposedBranchCreated");
        }
        finally
        {
            _applying = false;
        }
    }

    private sealed record ResultRow(int Id, string DisplayName, AiRunResultDto Result);
}
