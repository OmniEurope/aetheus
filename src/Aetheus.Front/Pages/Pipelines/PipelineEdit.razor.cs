// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Pages.Shared;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Pages.Pipelines;

public partial class PipelineEdit : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private PipelineRunGate RunGate { get; set; } = default!;
    [Inject] private PipelineRunDialogCoordinator RunDialogs { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private ProjectNavContextService ProjectNav { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;
    [Inject] private PipelineImportState ImportState { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private ILogger<PipelineEdit> Logger { get; set; } = default!;

    [Parameter] public int? Id { get; set; }

    [SupplyParameterFromQuery] public int? ProjectId { get; set; }
    [SupplyParameterFromQuery] public int? ServerId { get; set; }
    [SupplyParameterFromQuery] public int? EnvironmentId { get; set; }
    [SupplyParameterFromQuery] public int? ProjectServerId { get; set; }

    private PipelineDto? _pipeline;
    private PipelineSourceDto? _source;
    private List<string> _sourceBranches = [];
    private PipelineModel _model = new();
    private List<PipelineRunDto> _runs = [];
    private Aetheus.Front.Shared.AetheusDataGrid<PipelineRunDto>? _runsGrid;
    private int _runsTotalCount;
    private int _runsPage = 1;
    private int _runsPageSize = 25;

    private PipelineRunsLiveConnection? _liveRuns;
    private List<ProjectDto> _projects = [];
    private bool _isNew => Id is null or 0;
    private bool _saving;
    private bool _loading;
    private bool _validating;
    private bool? _yamlValid;
    private int _selectedTab;
    private int? _previousId = int.MinValue;
    private int _loadGeneration;
    private MonacoEditor? _monacoEditor;
    private bool _suggestionsNeeded;
    private bool _showDiff;
    // The pipeline whose read-only definition is peeked beside the editor (Ctrl+Click on a `pipeline:` ref).
    private string? _peekName;
    private string _savedYaml = string.Empty;
    private List<PipelineTemplateSummaryDto> _templates = [];
    private string? _baseTemplateYaml;
    private int? _selectedTemplateId;
    private List<string> _serverNames = [];
    private List<string> _libraryNames = [];
    private List<string> _vaultNames = [];
    private bool _canTemplateWrite;
    private PipelineFleetItemDto? _fleetItem;

    private string BackHref => PipelineRunPresentation.EditBackHref(ProjectId, ServerId);

    private bool HasTemplateReference => PipelineTemplateReferenceHelper.Parse(_model.YamlDefinition) is not null;
    private bool HasLegacyTemplateReference =>
        PipelineTemplateReferenceHelper.Parse(_model.YamlDefinition) is { Version: null };
    private PipelineTemplateEditorWorkflow TemplateEditor => new(Api, Dialog, L);
    private PipelineYamlEditorCoordinator YamlEditor => new(Api, L);

    protected override void OnInitialized()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshTemplatePermission();
    }

    private async Task LoadBaseTemplateYamlAsync()
        => _baseTemplateYaml = await TemplateEditor.LoadBaseYamlAsync(_model.YamlDefinition, _templates);

    private async Task PinLegacyTemplateAsync()
    {
        var pinnedYaml = TemplateEditor.Pin(_model.YamlDefinition, _templates);
        if (pinnedYaml is null) return;
        _model.YamlDefinition = pinnedYaml;
        await LoadBaseTemplateYamlAsync();
        _suggestionsNeeded = true;
    }

    private Task<List<PipelineTemplateSummaryDto>> LoadTemplatesSafeAsync() =>
        TemplateEditor.LoadTemplatesAsync();

    private void OnPermissionsChanged()
    {
        RefreshTemplatePermission();
        _ = InvokeAsync(StateHasChanged);
    }

    private void RefreshTemplatePermission() =>
        _canTemplateWrite = Permissions.CanWrite(ResourceType.PipelineTemplate);

    private async Task OnTemplateSelected(object? value)
    {
        if (value is not int templateId) return;
        var selection = await TemplateEditor.SelectAsync(templateId, _model.Name);
        if (selection is not { } application)
        {
            _selectedTemplateId = null;
            return;
        }
        _model.YamlDefinition = application.Yaml;
        _baseTemplateYaml = application.BaseYaml;
        _suggestionsNeeded = true;
        StateHasChanged();
    }

    private async Task OnExtractTemplate()
    {
        if (await TemplateEditor.ExtractAsync(Id!.Value, _model.Name, _model.YamlDefinition))
            await OnRefresh();
    }

    private async Task OnPromoteTemplate()
    {
        if (await TemplateEditor.PromoteAsync(Id!.Value)) await OnRefresh();
    }

    private async Task OnUpdateTemplate()
    {
        if (Id is null || _fleetItem?.LatestVersion is null) return;
        if (await TemplateEditor.UpdateAsync(Id.Value, _fleetItem.LatestVersion.Value))
            await OnRefresh();
    }

    private async Task LoadFleetItemAsync()
    {
        if (Id is null || _pipeline is null)
        {
            _fleetItem = null;
            return;
        }
        _fleetItem = await TemplateEditor.FindFleetItemAsync(Id.Value);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Id == _previousId)
        {
            // Same pipeline, only a query/tab/back navigation: BreadcrumbService clears its items on
            // every LocationChanged, so re-assert them (and keep the project nav pinned) without
            // reloading. Setting the crumb only on Id-change stranded an empty breadcrumb after a
            // same-component navigation (browser back to this page, tab switch, ...).
            ReassertNavContext();
            return;
        }
        _previousId = Id;
        var generation = Interlocked.Increment(ref _loadGeneration);

        if (_liveRuns is not null)
        {
            await _liveRuns.DisposeAsync();
            _liveRuns = null;
        }
        _pipeline = null;
        _source = null;
        _sourceBranches = [];
        _model = new PipelineModel();
        _runs = [];
        _runsTotalCount = 0;
        _runsPage = 1;
        _templates = [];
        _baseTemplateYaml = null;
        _selectedTemplateId = null;
        _serverNames = [];
        _libraryNames = [];
        _vaultNames = [];
        _fleetItem = null;
        _peekName = null;
        _savedYaml = string.Empty;
        _yamlValid = null;
        _showDiff = false;

        if (!_isNew)
        {
            _loading = true;
            var pipelineTask = Api.GetPipelineAsync(Id!.Value);
            var runsTask = Api.GetPipelineRunsPagedAsync(Id!.Value, 1, _runsPageSize);
            var projectsTask = Api.GetAllProjectsAsync();
            var sourceTask = Api.GetPipelineSourceAsync(Id.Value);
            var templatesTask = LoadTemplatesSafeAsync();

            await Task.WhenAll(pipelineTask, runsTask, projectsTask, sourceTask, templatesTask);
            if (generation != _loadGeneration) return;

            _pipeline = await pipelineTask;
            var runsPage = await runsTask;
            ApplyRunsPage(runsPage, 1, _runsPageSize);
            _projects = await projectsTask;
            _templates = await templatesTask;
            _source = await sourceTask;
            if (_source is { RepositoryId: > 0 })
            {
                var branches = await Api.GetGitBranchesAsync(_source.RepositoryId);
                if (generation != _loadGeneration) return;
                _sourceBranches = branches.Select(branch => branch.Name).ToList();
            }

            if (_pipeline is not null)
            {
                _model = new PipelineModel
                {
                    Name = _pipeline.Name,
                    Description = _pipeline.Description,
                    YamlDefinition = _pipeline.YamlDefinition,
                    ProjectId = _pipeline.ProjectId,
                    SourceBranch = _pipeline.SourceBranch ?? _source?.Branch,
                    EnvironmentId = _pipeline.EnvironmentId,
                    ProjectServerId = _pipeline.ProjectServerId
                };
                _savedYaml = _pipeline.YamlDefinition;
                await LoadBaseTemplateYamlAsync();
                await LoadFleetItemAsync();
                if (generation != _loadGeneration) return;
            }
            _loading = false;
            _suggestionsNeeded = true;

            await EnsureHubAsync();
            await _liveRuns!.SyncGroupsAsync();
        }
        else
        {
            var projectsTask = Api.GetAllProjectsAsync();
            var templatesTask = Api.GetPipelineTemplatesAsync();
            await Task.WhenAll(projectsTask, templatesTask);
            if (generation != _loadGeneration) return;
            _projects = await projectsTask;
            _templates = await templatesTask;

            // Pre-select the project when navigated from a project detail page (?projectId=NN).
            if (ProjectId is > 0 && _projects.Any(p => p.Id == ProjectId.Value))
                _model.ProjectId = ProjectId;
            else if (EnvironmentId is > 0)
                _model.EnvironmentId = EnvironmentId;
            else if (ProjectServerId is > 0)
                _model.ProjectServerId = ProjectServerId;

            _model.YamlDefinition = """
                name: my-pipeline
                trigger: manual
                variable_libraries:
                  - shared-config
                vaults:
                  - project-secrets
                variables:
                  APP_NAME: my-app
                stages:
                  - name: Build
                    jobs:
                      - name: build
                        agent: default
                        steps:
                          - name: Build
                            shell: echo "Building..."
                  - name: Deploy
                    depends_on: [Build]
                    jobs:
                      - name: deploy
                        agent: default
                        steps:
                          - name: Deploy
                            shell: echo "Deploying..."
                """;
            _suggestionsNeeded = true;

            // S-FEAT-17: a pipeline imported from a YAML file on the list page overrides the default
            // template; the user still picks an owner here before saving (ExactlyOneOwner).
            if (ImportState.Consume() is { } import)
            {
                _model.YamlDefinition = import.Yaml;
                if (!string.IsNullOrWhiteSpace(import.Name))
                    _model.Name = import.Name;
            }
        }

        ReassertNavContext();
    }

    // Publish the parent project so the NavMenu keeps the project's submenu open while we're editing
    // one of its pipelines, and (re)assert the breadcrumb. Called on every OnParametersSetAsync,
    // including same-component tab/back navigation, because BreadcrumbService clears on each
    // LocationChanged and would otherwise leave the crumb blank until the next full page load.
    private void ReassertNavContext()
    {
        ProjectNav.Set(_model.ProjectId);
        Breadcrumb.Set(
            new BreadcrumbItem(L["Pipelines"], "/pipelines"),
            new BreadcrumbItem(_isNew ? L["NewPipeline"] : _pipeline?.Name ?? L["Pipeline"]));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_suggestionsNeeded && _monacoEditor is not null)
        {
            _suggestionsNeeded = false;
            await LoadAutocompleteSuggestionsAsync(_model.ProjectId);
        }
    }

    private async Task OnSubmit()
    {
        if (!_isNew && !await ConfirmSaveAsync()) return;

        _saving = true;
        try
        {
            if (_isNew)
            {
                var outcome = await Api.CreatePipelineAsync(new CreatePipelineRequest
                {
                    Name = _model.Name,
                    Description = _model.Description,
                    YamlDefinition = _model.YamlDefinition,
                    ProjectId = _model.ProjectId,
                    SourceBranch = _model.SourceBranch,
                    EnvironmentId = _model.EnvironmentId,
                    ProjectServerId = _model.ProjectServerId
                });
                HandleSaveOutcome(outcome, "PipelineCreated", "Created",
                    created => Nav.NavigateTo($"/pipelines/{created.Id}"));
            }
            else
            {
                var outcome = await Api.UpdatePipelineAsync(Id!.Value, new UpdatePipelineRequest
                {
                    Name = _model.Name,
                    Description = _model.Description,
                    YamlDefinition = _model.YamlDefinition,
                    ProjectId = _model.ProjectId,
                    SourceBranch = _model.SourceBranch,
                    EnvironmentId = _model.EnvironmentId,
                    ProjectServerId = _model.ProjectServerId
                });
                HandleSaveOutcome(outcome, "PipelineSaved", "Saved", updated =>
                {
                    _pipeline = updated;
                    _savedYaml = _model.YamlDefinition;
                    _ = RefreshSourceAsync();
                });
            }
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task<bool> ConfirmSaveAsync()
    {
        var result = await Dialog.OpenAsync<PipelineSaveDialog>(
            L["ReviewPipelineChange"].Value,
            new Dictionary<string, object?>
            {
                { "SavedYaml", _savedYaml },
                { "DraftYaml", _model.YamlDefinition },
                { "Branches", _sourceBranches },
                { "SourceBranch", _model.SourceBranch }
            },
            new DialogOptions { Width = "900px", Height = "720px" });

        if (result is not PipelineSaveDecision decision) return false;
        _model.SourceBranch = decision.SourceBranch;
        return true;
    }

    private async Task RefreshSourceAsync()
    {
        if (Id is not { } id) return;
        _source = await Api.GetPipelineSourceAsync(id);
        if (_source is { RepositoryId: > 0 })
            _sourceBranches = (await Api.GetGitBranchesAsync(_source.RepositoryId)).Select(branch => branch.Name).ToList();
        await InvokeAsync(StateHasChanged);
    }

    // Surfaces the precise YAML validation errors (400) the API now returns on save instead of
    // a generic "save failed", keeping the contract identical to run/preflight.
    private void HandleSaveOutcome(
        ApiOutcome<PipelineDto, YamlValidationResultDto> outcome,
        string successKey, string successTitleKey, Action<PipelineDto> onSuccess)
    {
        if (outcome.Value is not null)
        {
            onSuccess(outcome.Value);
            Toast.Success(successTitleKey, successKey);
            return;
        }

        if (outcome.Error is not null)
            Toast.Notify(NotificationSeverity.Error, "Error", string.Join(" ", outcome.Error.Errors));
        else
            Toast.Error("Error", "SaveFailed");
    }

    private void OnYamlValueChanged(string value)
    {
        _model.YamlDefinition = value;
        _yamlValid = null;
    }

    private async Task OnDebouncedYamlChanged(string value)
    {
        await OnValidateYaml();
    }

    private async Task OnFormatYaml()
    {
        if (_monacoEditor is not null)
        {
            await _monacoEditor.FormatDocumentAsync();
        }
    }

    // Ctrl+Click on a `pipeline: <name>` ref in the editor opens that pipeline's read-only definition peek.
    private void OnEditorPipelineRef(string name) => _peekName = name;

    private async Task OnValidateYaml()
    {
        if (string.IsNullOrWhiteSpace(_model.YamlDefinition)) return;

        _validating = true;
        StateHasChanged();

        _yamlValid = await YamlEditor.ValidateAsync(_model.YamlDefinition, _monacoEditor);

        _validating = false;
        StateHasChanged();
    }

    private async Task LoadAutocompleteSuggestionsAsync(int? projectId)
    {
        var data = await YamlEditor.LoadSuggestionsAsync(projectId, _monacoEditor);
        _serverNames = data.ServerNames;
        _libraryNames = data.LibraryNames;
        _vaultNames = data.VaultNames;
    }

    private void OnVisualYamlChanged(string yaml)
    {
        _model.YamlDefinition = yaml;
        _yamlValid = null;
    }

    private bool _running;

    private async Task OnRefresh()
    {
        if (_isNew || Id is null) return;
        await LoadRunsPageAsync(_runsPage, _runsPageSize);
        _pipeline = await Api.GetPipelineAsync(Id.Value);
        if (_pipeline is not null)
        {
            _model.Name = _pipeline.Name;
            _model.Description = _pipeline.Description;
            _model.YamlDefinition = _pipeline.YamlDefinition;
            _model.ProjectId = _pipeline.ProjectId;
            _model.SourceBranch = _pipeline.SourceBranch ?? _source?.Branch;
            _model.EnvironmentId = _pipeline.EnvironmentId;
            _model.ProjectServerId = _pipeline.ProjectServerId;
            _savedYaml = _pipeline.YamlDefinition;
            await LoadBaseTemplateYamlAsync();
            await LoadFleetItemAsync();
        }
        if (_liveRuns is not null) await _liveRuns.SyncGroupsAsync();
    }

    private async Task OnRunsLoadDataAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        await LoadRunsPageAsync(page, pageSize);
        if (_liveRuns is not null) await _liveRuns.SyncGroupsAsync();
    }

    private async Task LoadRunsPageAsync(int page, int pageSize)
    {
        if (Id is null) return;
        var pipelineId = Id.Value;
        var result = await Api.GetPipelineRunsPagedAsync(pipelineId, page, pageSize);
        if (Id == pipelineId) ApplyRunsPage(result, page, pageSize);
    }

    private void ApplyRunsPage(PaginatedResult<PipelineRunDto> result, int page, int pageSize)
    {
        _runs = result.Items;
        _runsTotalCount = result.TotalCount;
        _runsPage = page;
        _runsPageSize = pageSize;
    }

    private async Task EnsureHubAsync()
    {
        if (_liveRuns is not null || Id is null) return;
        _liveRuns = new PipelineRunsLiveConnection(
            HubFactory,
            Logger,
            Id.Value,
            () => _runs.Where(run => run.Status is PipelineStatus.Running or PipelineStatus.Pending)
                .Select(run => run.Id).ToArray(),
            ReloadRunsAsync,
            InvokeAsync);
        await _liveRuns.StartAsync();
    }

    private Task OnPipelineRunStarted(int runId, int pipelineId) => pipelineId == Id
        ? ReloadRunsAsync()
        : Task.CompletedTask;

    private async Task ReloadRunsAsync()
    {
        if (Id is null) return;
        await LoadRunsPageAsync(_runsPage, _runsPageSize);
        _pipeline = await Api.GetPipelineAsync(Id.Value);
        if (_liveRuns is not null) await _liveRuns.SyncGroupsAsync();
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        Permissions.OnPermissionsChanged -= OnPermissionsChanged;
        if (_liveRuns is not null) await _liveRuns.DisposeAsync();
    }

    private Task OnRun() => OnRunAsync(null);

    private async Task OnRunClick(RadzenSplitButtonItem? item)
    {
        var sourceBranch = item?.Value == "branch"
            ? await RunDialogs.ChooseBranchAsync(Id!.Value)
            : null;
        if (item?.Value == "branch" && sourceBranch is null) return;
        await OnRunAsync(sourceBranch);
    }

    private async Task OnRunAsync(string? sourceBranch)
    {
        if (_running) return;
        _running = true;
        try
        {
            var launcher = new PipelineRunLauncher(Api, RunGate, RunDialogs, Nav, Toast, L);
            var result = await launcher.LaunchAsync(Id!.Value, sourceBranch);
            if (result is not null)
            {
                if (result.RunsPage is not null)
                    ApplyRunsPage(result.RunsPage, 1, 25);
                else if (_runs.All(run => run.Id != result.TriggeredRun.Id))
                    _runs.Insert(0, result.TriggeredRun);
                if (result.Pipeline is not null)
                    _pipeline = result.Pipeline;
            }
        }
        finally
        {
            _running = false;
        }
    }

    private async Task OnDryRun()
    {
        var result = await Api.DryRunPipelineAsync(Id!.Value);
        if (result is null) return;

        await Dialog.OpenAsync<DryRunResultDialog>(
            L["DryRunResult"].Value,
            new Dictionary<string, object?> { { "Result", result } },
            new DialogOptions { Width = "800px", Height = "600px" });
    }

    private async Task OnDelete()
    {
        var confirmed = await Dialog.Confirm(L["DeletePipelineConfirm"].Value, L["DeletePipeline"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed == true)
        {
            await Api.DeletePipelineAsync(Id!.Value);
            Cache.InvalidatePrefix("pipelines:"); // S-TECH-SWIV
            Nav.NavigateTo("/pipelines");
        }
    }

}
