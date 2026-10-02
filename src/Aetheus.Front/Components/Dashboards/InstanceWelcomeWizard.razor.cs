// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Dashboards;

/// <summary>
/// First-run checklist for a brand-new Aetheus instance. It only renders while the instance is
/// still being bootstrapped, which is why it may issue its own (tiny, near-empty) list calls: they
/// are skipped entirely as soon as the instance has grown past the first project.
/// </summary>
public partial class InstanceWelcomeWizard
{
    /// <summary>
    /// Above this project count the instance is no longer a fresh install, so neither the extra
    /// calls nor the checklist happen. The dashboard payload carries up to ten projects, so the
    /// comparison is exact in the range that matters.
    /// </summary>
    private const int FirstRunProjectCount = 1;

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    /// <summary>Projects already loaded by the host page; null while the page is still loading.</summary>
    [Parameter] public IReadOnlyList<ProjectDto>? Projects { get; set; }

    /// <summary>Total fleet servers already known to the host page.</summary>
    [Parameter] public int ServerCount { get; set; }

    private IReadOnlyList<OnboardingStep> _steps = [];
    private bool _show;
    private string? _loadedSignature;

    protected override async Task OnParametersSetAsync()
    {
        if (Projects is null)
        {
            Reset();
            return;
        }

        if (Projects.Count > FirstRunProjectCount)
        {
            Reset();
            return;
        }

        var signature = $"{Projects.Count}:{Projects.FirstOrDefault()?.Id}:{ServerCount}";
        if (signature == _loadedSignature) return;

        bool hasGitRepository;
        bool hasEnvironment;
        bool hasLibrary;
        try
        {
            var repositoriesTask = Api.Git.GetGitReposAsync();
            var environmentsTask = Api.Servers.GetAllEnvironmentsAsync();
            var librariesTask = Api.Variables.GetAllVariableLibrariesAsync();
            await Task.WhenAll(repositoriesTask, environmentsTask, librariesTask);
            hasGitRepository = (await repositoriesTask).Count > 0
                || Projects.Any(project => !string.IsNullOrWhiteSpace(project.RepositoryUrl));
            hasEnvironment = (await environmentsTask).Count > 0;
            hasLibrary = (await librariesTask).Count > 0;
        }
        catch (HttpRequestException)
        {
            // No proven state means no checklist: a guessed "done" would be worse than nothing.
            Reset();
            return;
        }

        _loadedSignature = signature;
        _steps = BuildSteps(hasGitRepository, hasEnvironment, hasLibrary);
        _show = _steps.Any(step => step.State != OnboardingStepState.Done);
    }

    private void Reset()
    {
        _loadedSignature = null;
        _steps = [];
        _show = false;
    }

    private List<OnboardingStep> BuildSteps(bool hasGitRepository, bool hasEnvironment, bool hasLibrary)
    {
        var project = Projects!.FirstOrDefault();
        var projectQuery = project is not null ? $"?projectId={project.Id}" : string.Empty;

        return
        [
            ProjectStep(project is not null),
            GitRepositoryStep(project is not null, hasGitRepository, projectQuery),
            EnvironmentStep(hasEnvironment, projectQuery),
            ServerStep(),
            LibraryStep(hasLibrary)
        ];
    }

    private OnboardingStep ProjectStep(bool hasProject) => new()
    {
        Title = L["CreateFirstProject"],
        Description = L["CreateFirstProjectHint"],
        Icon = "folder",
        State = hasProject ? OnboardingStepState.Done : OnboardingStepState.Todo,
        Href = "/projects",
        ActionLabel = hasProject ? L["Projects"] : L["NewProject"]
    };

    private OnboardingStep GitRepositoryStep(bool hasProject, bool hasGitRepository, string projectQuery) => new()
    {
        Title = L["CreateGitRepository"],
        Description = L["CreateGitRepositoryHint"],
        Icon = "commit",
        // A Git repository is stored against a mandatory ProjectId, so it genuinely cannot
        // exist before the first project.
        State = !hasProject
            ? OnboardingStepState.Blocked
            : hasGitRepository ? OnboardingStepState.Done : OnboardingStepState.Todo,
        BlockedReason = L["OnboardingBlockedNoProject"],
        Href = hasGitRepository
            ? $"/git-repositories{projectQuery}"
            : $"/git-repositories{projectQuery}{(hasProject ? "&" : "?")}create=true",
        ActionLabel = hasGitRepository ? L["GitRepositories"] : L["CreateGitRepository"]
    };

    private OnboardingStep EnvironmentStep(bool hasEnvironment, string projectQuery) => new()
    {
        Title = L["CreateEnvironment"],
        Description = L["CreateEnvironmentHint"],
        Icon = "layers",
        State = hasEnvironment ? OnboardingStepState.Done : OnboardingStepState.Todo,
        Href = hasEnvironment ? "/environments" : $"/environments/new{projectQuery}",
        ActionLabel = hasEnvironment ? L["Environments"] : L["CreateEnvironment"]
    };

    private OnboardingStep ServerStep() => new()
    {
        Title = L["AttachFirstServer"],
        Description = L["AttachFirstServerHint"],
        Icon = "dns",
        State = ServerCount > 0 ? OnboardingStepState.Done : OnboardingStepState.Todo,
        Href = ServerCount > 0 ? "/servers" : "/servers/add-agent",
        ActionLabel = ServerCount > 0 ? L["Servers"] : L["AddServer"]
    };

    private OnboardingStep LibraryStep(bool hasLibrary) => new()
    {
        Title = L["CreateVariableLibrary"],
        Description = L["CreateVariableLibraryHint"],
        Icon = "library_books",
        State = hasLibrary ? OnboardingStepState.Done : OnboardingStepState.Todo,
        Href = hasLibrary ? "/variable-libraries" : "/variable-libraries/new",
        ActionLabel = hasLibrary ? L["VariableLibraries"] : L["CreateVariableLibrary"]
    };
}
