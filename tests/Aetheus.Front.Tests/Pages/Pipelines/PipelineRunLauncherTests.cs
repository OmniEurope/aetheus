// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public sealed class PipelineRunLauncherTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler handler;

    public PipelineRunLauncherTests()
        => handler = BunitTestHelper.RegisterServices(this, isAdmin: true);

    [Fact]
    public async Task LaunchAsync_TriggerRefusal_SurfacesServerReasonInsteadOfGenericMessage()
    {
        handler.SetJsonResponse("api/pipelines/5/preflight", new PipelinePreflightDto());
        handler.SetJsonResponse("api/pipelines/5/parameters", new List<PipelineRunParameterDto>());
        handler.SetJsonResponse(
            HttpMethod.Post,
            "api/pipelines/5/run",
            new YamlValidationResultDto { Errors = ["Matrix axis 'bad-axis' is invalid."] },
            HttpStatusCode.BadRequest);
        var launcher = new PipelineRunLauncher(
            Services.GetRequiredService<ApiClient>(),
            Services.GetRequiredService<PipelineRunGate>(),
            Services.GetRequiredService<PipelineRunDialogCoordinator>(),
            Services.GetRequiredService<NavigationManager>(),
            Services.GetRequiredService<NotifyHelper>(),
            Services.GetRequiredService<IStringLocalizer<AppStrings>>());

        var result = await launcher.LaunchAsync(5, sourceBranch: null);

        Assert.Null(result);
        var notification = Assert.Single(
            Services.GetRequiredService<NotificationService>().Messages,
            message => message.Severity == NotificationSeverity.Error);
        Assert.Contains("bad-axis", notification.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("PipelineRunFailedNoReason", notification.Detail, StringComparison.Ordinal);
    }
}
