// SPDX-License-Identifier: EUPL-1.2
using Bunit;

namespace Aetheus.Front.Tests.Shared;

public class InstanceWelcomeWizardTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public InstanceWelcomeWizardTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        SetInstanceContent(repositories: [], environments: [], libraries: []);
    }

    private void SetInstanceContent(
        List<GitLightRepoDto> repositories,
        List<EnvironmentDto> environments,
        List<VariableLibraryDto> libraries)
    {
        _handler.SetPaginatedJsonResponse(HttpMethod.Get, "api/git/repos", repositories);
        _handler.SetPaginatedJsonResponse(HttpMethod.Get, "api/environments", environments);
        _handler.SetPaginatedJsonResponse(HttpMethod.Get, "api/variable-libraries", libraries);
    }

    private static List<string> StateBadges(IRenderedComponent<InstanceWelcomeWizard> cut) =>
        cut.FindAll("tr[data-testid='onboarding-step'] .omni-badge")
            .Select(badge => badge.TextContent.Trim())
            .ToList();

    [Fact]
    public void EmptyInstance_ListsEveryStepTodo_AndBlocksTheGitStepWithItsReason()
    {
        var cut = Render<InstanceWelcomeWizard>(p => p
            .Add(x => x.Projects, [])
            .Add(x => x.ServerCount, 0));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(5, cut.FindAll("tr[data-testid='onboarding-step']").Count);
            Assert.Equal(
                ["OnboardingStepTodo", "OnboardingStepBlocked", "OnboardingStepTodo", "OnboardingStepTodo", "OnboardingStepTodo"],
                StateBadges(cut));
            Assert.Contains("OnboardingBlockedNoProject", cut.Markup);
            Assert.Contains("InstanceGettingStartedTitle", cut.Markup);
        });
    }

    [Fact]
    public void PartiallySetUpInstance_MixesDoneAndTodoStatesFromTheLoadedData()
    {
        SetInstanceContent(
            repositories: [],
            environments: [new EnvironmentDto { Id = 4, Name = "Staging" }],
            libraries: []);

        var cut = Render<InstanceWelcomeWizard>(p => p
            .Add(x => x.Projects, [new ProjectDto { Id = 7, Name = "Web" }])
            .Add(x => x.ServerCount, 2));

        cut.WaitForAssertion(() =>
        {
            // project done, git still to do (project exists so it is no longer blocked),
            // environment done, server done, library still to do.
            Assert.Equal(
                ["Done", "OnboardingStepTodo", "Done", "Done", "OnboardingStepTodo"],
                StateBadges(cut));
            Assert.DoesNotContain("OnboardingBlockedNoProject", cut.Markup);
            Assert.Contains("/git-repositories?projectId=7&amp;create=true", cut.Markup);
            Assert.Contains("/variable-libraries/new", cut.Markup);
        });
    }

    [Fact]
    public void FullySetUpInstance_RendersNothing()
    {
        SetInstanceContent(
            repositories: [new GitLightRepoDto { Id = 1, ProjectId = 7, Name = "web" }],
            environments: [new EnvironmentDto { Id = 4, Name = "Staging" }],
            libraries: [new VariableLibraryDto { Id = 9, Name = "Shared" }]);

        var cut = Render<InstanceWelcomeWizard>(p => p
            .Add(x => x.Projects, [new ProjectDto { Id = 7, Name = "Web" }])
            .Add(x => x.ServerCount, 1));

        cut.WaitForAssertion(() => Assert.Empty(cut.Markup.Trim()));
    }

    [Fact]
    public void PopulatedInstance_RendersNothing_AndIssuesNoDiscoveryCall()
    {
        var cut = Render<InstanceWelcomeWizard>(p => p
            .Add(x => x.Projects, [new ProjectDto { Id = 1 }, new ProjectDto { Id = 2 }])
            .Add(x => x.ServerCount, 0));

        Assert.Empty(cut.Markup.Trim());
        Assert.DoesNotContain(_handler.Requests, request => request.Url.Contains("api/git/repos", StringComparison.Ordinal));
        Assert.DoesNotContain(_handler.Requests, request => request.Url.Contains("api/environments", StringComparison.Ordinal));
        Assert.DoesNotContain(_handler.Requests, request => request.Url.Contains("api/variable-libraries", StringComparison.Ordinal));
    }

    [Fact]
    public void UnloadedHostPage_RendersNothing()
    {
        var cut = Render<InstanceWelcomeWizard>(p => p
            .Add(x => x.Projects, (IReadOnlyList<ProjectDto>?)null)
            .Add(x => x.ServerCount, 0));

        Assert.Empty(cut.Markup.Trim());
        Assert.DoesNotContain(_handler.Requests, request => request.Url.Contains("api/git/repos", StringComparison.Ordinal));
    }

    [Fact]
    public void ExternalRepositoryUrlOnTheProject_CountsTheGitStepAsDone()
    {
        var cut = Render<InstanceWelcomeWizard>(p => p
            .Add(x => x.Projects, [new ProjectDto { Id = 7, Name = "Web", RepositoryUrl = "https://example.test/web.git" }])
            .Add(x => x.ServerCount, 0));

        cut.WaitForAssertion(() =>
            Assert.Equal(
                ["Done", "Done", "OnboardingStepTodo", "OnboardingStepTodo", "OnboardingStepTodo"],
                StateBadges(cut)));
    }
}
