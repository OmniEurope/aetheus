// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Pages.Artifacts;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Artifacts;

public class ArtifactDetailTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ArtifactDetailTests() => _handler = BunitTestHelper.RegisterServices(this);

    private static PipelineArtifactDto Sample(int id = 5) => new()
    {
        Id = id,
        Name = "build-output",
        ProjectId = 1,
        ProjectName = "Demo",
        PipelineId = 1,
        PipelineName = "CI",
        PipelineRunId = 42,
        SizeBytes = 2_097_152,
        RetentionPolicy = ArtifactRetentionPolicy.Released,
        CreatedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        RetentionExpiresAt = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
        CommitHash = "abcdef1234567890",
        BranchName = "main",
        RepositoryUrl = "https://github.com/acme/demo.git",
        Sha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"
    };

    [Fact]
    public void Renders_Artifact_ShowsChecksum()
    {
        _handler.SetJsonResponse("api/artifacts/5", Sample());

        var cut = Render<ArtifactDetail>(p => p.Add(c => c.ArtifactId, 5));

        cut.WaitForState(() => cut.Markup.Contains("build-output"));
        Assert.Contains("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", cut.Markup);
    }

    [Fact]
    public void Renders_ArtifactWithoutChecksum_ShowsUnavailableNotFabricated()
    {
        // A metadata-only artifact (no stored bytes) must honestly show "not available",
        // never a placeholder or fabricated hash.
        _handler.SetJsonResponse("api/artifacts/6", Sample(6) with { Sha256 = null });

        var cut = Render<ArtifactDetail>(p => p.Add(c => c.ArtifactId, 6));

        cut.WaitForState(() => cut.Markup.Contains("build-output"));
        // Stub localizer echoes keys; the honest "unavailable" branch renders ChecksumUnavailable.
        Assert.Contains("ChecksumUnavailable", cut.Markup);
    }

    [Fact]
    public void Renders_LoadedArtifact_ShowsName()
    {
        _handler.SetJsonResponse("api/artifacts/5", Sample());

        var cut = Render<ArtifactDetail>(p => p.Add(c => c.ArtifactId, 5));

        cut.WaitForState(() => cut.Markup.Contains("build-output"));
        Assert.Contains("build-output", cut.Markup);
    }

    [Fact]
    public void Renders_NotFound_DoesNotShowArtifactName()
    {
        _handler.SetResponse("api/artifacts/9", HttpStatusCode.NotFound);

        var cut = Render<ArtifactDetail>(p => p.Add(c => c.ArtifactId, 9));

        Assert.DoesNotContain("build-output", cut.Markup);
    }

    [Fact]
    public void ArtifactIdChange_ReloadsSameComponentInstance()
    {
        _handler.SetJsonResponse("api/artifacts/1", Sample(1) with { Name = "first-artifact" });
        _handler.SetJsonResponse("api/artifacts/2", Sample(2) with { Name = "second-artifact" });
        var cut = Render<ArtifactDetail>(p => p.Add(c => c.ArtifactId, 1));

        cut.Render(p => p.Add(c => c.ArtifactId, 2));

        cut.WaitForAssertion(() => Assert.Contains("second-artifact", cut.Markup));
        Assert.DoesNotContain("first-artifact", cut.Markup);
    }
}
