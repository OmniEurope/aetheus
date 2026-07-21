// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Projects;

public class ProjectExternalRepoSectionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectExternalRepoSectionTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public void FeatureDisabled_ShowsDisabledNotice()
    {
        _handler.SetJsonResponse("api/external-repos/enabled", false);

        var cut = Render<ProjectExternalRepoSection>(p => p.Add(c => c.ProjectId, 1));

        cut.WaitForState(() => cut.Markup.Contains("ExternalRepoDisabled"), TimeSpan.FromSeconds(3));
        Assert.Contains("ExternalRepoDisabled", cut.Markup);
    }

    [Fact]
    public void EnabledNotAttached_ShowsAttachForm()
    {
        _handler.SetJsonResponse("api/external-repos/enabled", true);
        _handler.SetResponse("api/external-repos/project/1", HttpStatusCode.NotFound);

        var cut = Render<ProjectExternalRepoSection>(p => p.Add(c => c.ProjectId, 1));

        cut.WaitForState(() => cut.Markup.Contains("OwnerOrGroup"), TimeSpan.FromSeconds(3));
        Assert.Contains("Attach", cut.Markup);
    }

    [Fact]
    public void EnabledAttached_ShowsMirrorStatus()
    {
        _handler.SetJsonResponse("api/external-repos/enabled", true);
        _handler.SetJsonResponse("api/external-repos/project/1", new ExternalRepoDto
        {
            ProjectId = 1,
            ProviderType = GitProviderType.GitHub,
            OwnerOrGroup = "acme",
            RepositoryName = "demo",
            MirrorStatus = GitMirrorStatus.Ready
        });

        var cut = Render<ProjectExternalRepoSection>(p => p.Add(c => c.ProjectId, 1));

        cut.WaitForState(() => cut.Markup.Contains("acme"), TimeSpan.FromSeconds(3));
        Assert.Contains("Enum_GitMirrorStatus_Ready", cut.Markup);
        Assert.Contains("SyncNow", cut.Markup);
    }
}
