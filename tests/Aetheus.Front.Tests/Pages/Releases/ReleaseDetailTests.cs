// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Pages.Releases;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Releases;

public class ReleaseDetailTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ReleaseDetailTests() => _handler = BunitTestHelper.RegisterServices(this);

    private static ReleaseDto Sample(int id = 7) => new()
    {
        Id = id,
        ProjectId = 1,
        ProjectName = "Demo",
        Version = "v2.3.0",
        BranchName = "main",
        DetectedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        PublishedAt = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
        BuildNumber = 128,
        CommitHash = "abcdef1234567890",
        TagName = "v2.3.0",
        RepositoryUrl = "https://github.com/acme/demo.git",
        Changelog = "- feat: add dashboard\n- fix: null crash\n- chore: bump deps"
    };

    [Fact]
    public void Renders_LoadedRelease_ShowsVersion()
    {
        _handler.SetJsonResponse("api/releases/7", Sample());

        var cut = Render<ReleaseDetail>(p => p.Add(c => c.ReleaseId, 7));

        cut.WaitForState(() => cut.Markup.Contains("v2.3.0"));
        Assert.Contains("v2.3.0", cut.Markup);
    }

    [Fact]
    public void Renders_NotFound_DoesNotShowVersion()
    {
        _handler.SetResponse("api/releases/9", HttpStatusCode.NotFound);

        var cut = Render<ReleaseDetail>(p => p.Add(c => c.ReleaseId, 9));

        Assert.DoesNotContain("v2.3.0", cut.Markup);
    }

    [Fact]
    public void ReleaseIdChange_RequestsSecondReleaseOnSameInstance()
    {
        _handler.SetResponse("api/releases/1", HttpStatusCode.NotFound);
        _handler.SetResponse("api/releases/2", HttpStatusCode.NotFound);
        var cut = Render<ReleaseDetail>(p => p.Add(c => c.ReleaseId, 1));

        cut.Render(p => p.Add(c => c.ReleaseId, 2));

        Assert.Contains(_handler.Requests, request => request.Url.Contains("api/releases/1"));
        Assert.Contains(_handler.Requests, request => request.Url.Contains("api/releases/2"));
    }
}
