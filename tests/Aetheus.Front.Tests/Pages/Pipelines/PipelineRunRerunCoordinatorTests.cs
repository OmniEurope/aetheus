// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen.Blazor;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public sealed class PipelineRunRerunCoordinatorTests : BunitContext
{
    [Fact]
    public async Task ResumeCheckpoints_UsesExplicitRerunModeWithoutParameterDialog()
    {
        var handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
        handler.SetJsonResponse(HttpMethod.Get, "api/pipelines/runs/42/checkpoint-resume-preview",
            new PipelineCheckpointResumePreviewDto
            {
                SourceRunId = 42,
                Items =
                [
                    new PipelineCheckpointResumeItemDto
                    {
                        PipelineName = "aetheus-ci",
                        RunId = 40,
                        ReuseCandidate = true
                    }
                ]
            });
        handler.SetJsonResponse(HttpMethod.Post, "api/pipelines/runs/42/rerun",
            new PipelineRunDto { Id = 99 });
        var coordinator = new PipelineRunRerunCoordinator(
            Services.GetRequiredService<ApiClient>(),
            Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>(),
            Services.GetRequiredService<NotifyHelper>(),
            Services.GetRequiredService<Radzen.DialogService>(),
            new BunitTestHelper.StubLocalizer());
        ((Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services
            .GetRequiredService<Radzen.DialogService>()).ConfirmResult = true;
        var run = new PipelineRunDto { Id = 42, PipelineId = 5, Status = PipelineStatus.Failed };

        var item = new RadzenSplitButtonItem();
        typeof(RadzenSplitButtonItem).GetProperty(nameof(RadzenSplitButtonItem.Value))!
            .SetValue(item, "resumeCheckpoints");
        await coordinator.RerunAsync(run, item);

        Assert.Contains(handler.Requests, request =>
            request.Url.Contains("api/pipelines/runs/42/rerun?mode=ResumeCheckpoints", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, request =>
            request.Url.Contains("api/pipelines/5/parameters", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ParameterLoadFailure_DoesNotFallBackToDirectRerun()
    {
        var handler = BunitTestHelper.RegisterServices(this);
        handler.SetResponse("api/pipelines/5/parameters",
            System.Net.HttpStatusCode.InternalServerError);
        handler.SetJsonResponse(HttpMethod.Post, "api/pipelines/runs/42/rerun",
            new PipelineRunDto { Id = 99 });
        var coordinator = new PipelineRunRerunCoordinator(
            Services.GetRequiredService<ApiClient>(),
            Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>(),
            Services.GetRequiredService<NotifyHelper>(),
            Services.GetRequiredService<Radzen.DialogService>(),
            new BunitTestHelper.StubLocalizer());
        var run = new PipelineRunDto
        {
            Id = 42,
            PipelineId = 5,
            Status = PipelineStatus.Failed
        };

        await coordinator.RerunAsync(run, null);

        Assert.Contains(handler.Requests, request =>
            request.Url.Contains("api/pipelines/5/parameters", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, request =>
            request.Url.Contains("api/pipelines/runs/42/rerun", StringComparison.Ordinal));
    }
}
