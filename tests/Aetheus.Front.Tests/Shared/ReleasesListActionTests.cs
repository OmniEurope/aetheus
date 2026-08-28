// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Aetheus.Front.Shared;
using Aetheus.Front.Tests.TestDoubles;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// The three release actions that go through a dialog: triggering a build, promoting, and rolling
/// back. All of them were unreachable in tests because the real Radzen dialog never completes under
/// bUnit, so the rules that matter were unprotected: a rollback must be refused outright when the
/// preview says it cannot restore anything, and a promotion must not happen without a confirmation.
/// </summary>
public sealed class ReleasesListActionTests : BunitContext
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BunitTestHelper.TestHandler _handler;
    private readonly ImmediateDialogService _dialog;

    public ReleasesListActionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
        _dialog = (ImmediateDialogService)Services.GetRequiredService<DialogService>();
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "aetheus", Status = ProjectStatus.Active }],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/pipelines", new PaginatedResult<PipelineDto>
        {
            Items = [new PipelineDto { Id = 3, Name = "deploy", ProjectId = 1 }],
            TotalCount = 1
        });
    }

    private void WireReleases(params ReleaseDto[] releases) =>
        _handler.SetJsonResponse("api/releases", new PaginatedResult<ReleaseDto>
        {
            Items = [.. releases],
            TotalCount = releases.Length
        });

    private static ReleaseDto Release(
        int id = 1, string version = "1.0.0", ReleaseStatus status = ReleaseStatus.Published) =>
        new()
        {
            Id = id,
            ProjectId = 1,
            ProjectName = "aetheus",
            Version = version,
            Status = status,
            PublishedAt = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)
        };

    private IRenderedComponent<ReleasesList> RenderList(params ReleaseDto[] releases)
    {
        WireReleases(releases);
        var cut = Render<ReleasesList>();
        cut.WaitForState(
            () => cut.Markup.Contains(releases[0].Version, StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));
        return cut;
    }

    private static IElement Action(IRenderedComponent<ReleasesList> cut, string title) =>
        cut.FindAll("button").First(button =>
            string.Equals(button.GetAttribute("title"), title, StringComparison.Ordinal));

    private bool Sent(string method, string urlContains) =>
        _handler.Requests.Any(request => request.Method == method
            && request.Url.Contains(urlContains, StringComparison.Ordinal));

    private T LastBody<T>(string method, string urlContains)
    {
        var body = _handler.RequestDetails
            .Last(request => request.Method == method
                && request.Url.Contains(urlContains, StringComparison.Ordinal))
            .Body;
        return JsonSerializer.Deserialize<T>(body!, Json)!;
    }

    // ---------- trigger build ----------

    [Fact]
    public void TriggeringABuildSendsTheSelectedPipeline()
    {
        _handler.SetJsonResponse(HttpMethod.Post, "api/releases/1/build", Release());
        _dialog.OpenResult = 3;
        var cut = RenderList(Release(status: ReleaseStatus.Detected));

        Action(cut, "TriggerBuild").Click();

        cut.WaitForAssertion(() => Assert.True(Sent("POST", "api/releases/1/build")), TimeSpan.FromSeconds(2));
        Assert.Equal(3, LastBody<TriggerReleaseBuildRequest>("POST", "api/releases/1/build").PipelineId);
    }

    [Fact]
    public void CancellingThePipelineChoiceSendsNothing()
    {
        _dialog.OpenResult = null;
        var cut = RenderList(Release(status: ReleaseStatus.Detected));

        Action(cut, "TriggerBuild").Click();

        Assert.False(Sent("POST", "api/releases/1/build"));
    }

    [Fact]
    public void TriggeringABuildWithNoPipelineAtAllNeverOpensTheChooser()
    {
        _handler.SetJsonResponse("api/pipelines", new PaginatedResult<PipelineDto> { Items = [], TotalCount = 0 });
        var cut = RenderList(Release(status: ReleaseStatus.Detected));

        Action(cut, "TriggerBuild").Click();

        cut.WaitForAssertion(() => Assert.Equal(0, _dialog.OpenCount), TimeSpan.FromSeconds(2));
        Assert.False(Sent("POST", "api/releases/1/build"));
    }

    // ---------- promote ----------

    [Fact]
    public void PromotingIsGatedByAConfirmation()
    {
        _dialog.ConfirmResult = false;
        var cut = RenderList(Release());

        Action(cut, "Promote").Click();

        Assert.False(Sent("POST", "api/releases/1/promote"));
    }

    [Fact]
    public void ConfirmingThePromotionCallsTheEndpoint()
    {
        _handler.SetJsonResponse(HttpMethod.Post, "api/releases/1/promote", Release());
        _dialog.ConfirmResult = true;
        var cut = RenderList(Release());

        Action(cut, "Promote").Click();

        cut.WaitForAssertion(() => Assert.True(Sent("POST", "api/releases/1/promote")), TimeSpan.FromSeconds(2));
    }

    // ---------- rollback ----------

    [Fact]
    public void RollingBackRefusesWhenThePreviewSaysItCannotRestoreAnything()
    {
        _handler.SetJsonResponse("api/releases/1/rollback-preview", new ReleaseRollbackPreviewDto
        {
            CanRollback = false,
            Reason = "The previous release artifact is no longer available in storage."
        });
        var cut = RenderList(Release());

        Action(cut, "Rollback").Click();

        cut.WaitForAssertion(
            () => Assert.True(Sent("GET", "api/releases/1/rollback-preview")), TimeSpan.FromSeconds(2));
        Assert.Equal(0, _dialog.OpenCount);
        Assert.False(Sent("POST", "api/releases/1/rollback"));
    }

    [Fact]
    public void RollingBackRefusesAPreviewThatNamesNoTargetVersion()
    {
        _handler.SetJsonResponse("api/releases/1/rollback-preview", new ReleaseRollbackPreviewDto
        {
            CanRollback = true,
            TargetVersion = "   "
        });
        var cut = RenderList(Release());

        Action(cut, "Rollback").Click();

        cut.WaitForAssertion(
            () => Assert.True(Sent("GET", "api/releases/1/rollback-preview")), TimeSpan.FromSeconds(2));
        Assert.False(Sent("POST", "api/releases/1/rollback"));
    }

    [Fact]
    public void RollingBackOpensTheDialogWithTheTargetVersionAndSendsWhatItReturns()
    {
        _handler.SetJsonResponse("api/releases/1/rollback-preview", new ReleaseRollbackPreviewDto
        {
            CanRollback = true,
            TargetVersion = "0.9.0",
            TargetPublishedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc)
        });
        _handler.SetJsonResponse(HttpMethod.Post, "api/releases/1/rollback", new ReleaseRollbackDto
        {
            Id = 9,
            SourceReleaseId = 1,
            TargetReleaseId = 2
        });
        _dialog.OpenResult = new RollbackReleaseRequest { PipelineId = 3, RestoreDatabase = false };
        var cut = RenderList(Release());

        Action(cut, "Rollback").Click();

        cut.WaitForAssertion(() => Assert.True(Sent("POST", "api/releases/1/rollback")), TimeSpan.FromSeconds(2));
        Assert.Equal("0.9.0", Assert.Contains("TargetVersion", _dialog.LastParameters!));
        Assert.Equal("1.0.0", Assert.Contains("SourceVersion", _dialog.LastParameters!));
        Assert.Equal(3, LastBody<RollbackReleaseRequest>("POST", "api/releases/1/rollback").PipelineId);
    }

    [Fact]
    public void CancellingTheRollbackDialogSendsNothing()
    {
        _handler.SetJsonResponse("api/releases/1/rollback-preview", new ReleaseRollbackPreviewDto
        {
            CanRollback = true,
            TargetVersion = "0.9.0"
        });
        _dialog.OpenResult = null;
        var cut = RenderList(Release());

        Action(cut, "Rollback").Click();

        cut.WaitForAssertion(
            () => Assert.True(Sent("GET", "api/releases/1/rollback-preview")), TimeSpan.FromSeconds(2));
        Assert.False(Sent("POST", "api/releases/1/rollback"));
    }

    /// <summary>
    /// Documents the current behaviour rather than a desired one: the preview call is NOT wrapped in
    /// a try/catch, so a transport failure surfaces to the renderer instead of degrading into the
    /// "rollback unavailable" toast. The rollback itself is still never sent, which is what matters
    /// for safety; the missing guard is a separate UX decision, not something this test invents.
    /// </summary>
    [Fact]
    public void RollingBackSurfacesAFailedPreviewInsteadOfSendingTheRollback()
    {
        _handler.SetResponse(HttpMethod.Get, "api/releases/1/rollback-preview", HttpStatusCode.NotFound);
        var cut = RenderList(Release());

        Assert.ThrowsAny<Exception>(() => Action(cut, "Rollback").Click());

        Assert.True(Sent("GET", "api/releases/1/rollback-preview"));
        Assert.False(Sent("POST", "api/releases/1/rollback"));
    }

    // ---------- refresh and sync ----------

    [Fact]
    public void RefreshingReloadsTheGrid()
    {
        var cut = RenderList(Release());
        var before = _handler.Requests.Count(request =>
            request.Url.Contains("api/releases", StringComparison.Ordinal));

        Action(cut, "Refresh").Click();

        cut.WaitForAssertion(
            () => Assert.True(_handler.Requests.Count(request =>
                request.Url.Contains("api/releases", StringComparison.Ordinal)) > before),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SyncingInsideAProjectPostsToThatProject()
    {
        WireReleases(Release());
        _handler.SetJsonResponse(HttpMethod.Post, "api/releases/sync/1", new List<ReleaseDto> { Release(2, "1.1.0") });
        var cut = Render<ReleasesList>(parameters => parameters.Add(list => list.ProjectId, 1));
        cut.WaitForState(() => cut.Markup.Contains("1.0.0", StringComparison.Ordinal), TimeSpan.FromSeconds(2));

        Action(cut, "SyncReleases").Click();

        cut.WaitForAssertion(() => Assert.True(Sent("POST", "api/releases/sync/1")), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SyncingReportsAnEmptyResultWithoutFailing()
    {
        WireReleases(Release());
        _handler.SetJsonResponse(HttpMethod.Post, "api/releases/sync/1", new List<ReleaseDto>());
        var cut = Render<ReleasesList>(parameters => parameters.Add(list => list.ProjectId, 1));
        cut.WaitForState(() => cut.Markup.Contains("1.0.0", StringComparison.Ordinal), TimeSpan.FromSeconds(2));

        Action(cut, "SyncReleases").Click();

        cut.WaitForAssertion(() => Assert.True(Sent("POST", "api/releases/sync/1")), TimeSpan.FromSeconds(2));
        Assert.Contains("1.0.0", cut.Markup);
    }
}
