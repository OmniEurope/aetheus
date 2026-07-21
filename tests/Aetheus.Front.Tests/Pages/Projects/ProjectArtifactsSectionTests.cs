// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Projects.ProjectDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Projects;

public class ProjectArtifactsSectionTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ProjectArtifactsSectionTests() => _handler = BunitTestHelper.RegisterServices(this);

    private static PaginatedResult<PipelineArtifactDto> Page(params string[] names) => new()
    {
        Items = names.Select((n, i) => new PipelineArtifactDto
        {
            Id = i + 1,
            Name = n,
            ProjectId = 1,
            PipelineName = "CI",
            SizeBytes = 1024,
            RetentionPolicy = ArtifactRetentionPolicy.Build,
            CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            RetentionExpiresAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        }).ToList(),
        TotalCount = names.Length
    };

    [Fact]
    public void Renders_ArtifactsList_ShowsArtifactName()
    {
        _handler.SetJsonResponse("api/artifacts/project/1", Page("nuget-package", "docker-image"));

        var cut = Render<ProjectArtifactsSection>(p => p.Add(c => c.ProjectId, 1));

        cut.WaitForState(() => cut.Markup.Contains("nuget-package"));
        Assert.Contains("nuget-package", cut.Markup);
    }

    [Fact]
    public void Renders_EmptyList_WithoutError()
    {
        _handler.SetJsonResponse("api/artifacts/project/2", Page());

        var cut = Render<ProjectArtifactsSection>(p => p.Add(c => c.ProjectId, 2));

        Assert.DoesNotContain("nuget-package", cut.Markup);
    }

    [Fact]
    public void ProjectIdChange_ReloadsArtifactsOnSameInstance()
    {
        _handler.SetJsonResponse("api/artifacts/project/1", Page("first-artifact"));
        _handler.SetJsonResponse("api/artifacts/project/2", Page("second-artifact"));
        var cut = Render<ProjectArtifactsSection>(p => p.Add(c => c.ProjectId, 1));

        cut.Render(p => p.Add(c => c.ProjectId, 2));

        cut.WaitForAssertion(() => Assert.Contains("second-artifact", cut.Markup));
        Assert.DoesNotContain("first-artifact", cut.Markup);
    }
}
