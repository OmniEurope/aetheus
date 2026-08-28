// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Git;

public partial class CommitDetail
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private Layout.ProjectNavContextService ProjectNav { get; set; } = default!;

    [Parameter] public int CommitId { get; set; }

    private int? _loadedCommitId;
    private bool _loading;
    private int? _projectId;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedCommitId == CommitId) return;
        _loadedCommitId = CommitId;
        var commitId = CommitId;
        _loading = true;
        GitCommitDto? commit;
        try { commit = await Api.Git.GetGitCommitAsync(commitId); }
        catch (HttpRequestException) { commit = null; }
        if (CommitId != commitId) return;
        if (commit is null)
        {
            _loading = false;
            return;
        }

        _projectId = commit.ProjectId;
        ProjectNav.Set(commit.ProjectId);
        List<GitLightRepoDto> repositories;
        try { repositories = await Api.Git.GetGitReposAsync(commit.ProjectId); }
        catch (HttpRequestException) { repositories = []; }
        if (CommitId != commitId) return;

        var repositoryId = GitRepositorySelection.Resolve(repositories, commit.RepositoryUrl);
        var target = repositoryId is { } id
            ? $"/git-repositories/{id}/commits/{Uri.EscapeDataString(commit.Sha)}"
            : $"/git-repositories?projectId={commit.ProjectId}";
        Nav.NavigateTo(target, replace: true);
    }
}
