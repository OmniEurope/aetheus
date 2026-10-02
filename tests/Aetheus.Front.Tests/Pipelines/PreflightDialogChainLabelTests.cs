// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Pipelines;
using Bunit;

namespace Aetheus.Front.Tests.Pipelines;

/// <summary>
/// The preflight preview now follows the trigger chain, so the blocked stages the dialog lists no
/// longer all belong to the pipeline the user is looking at. A bare "Deploy" badge on an
/// aetheus-candidate launch names a stage that pipeline does not have.
/// </summary>
public sealed class PreflightDialogChainLabelTests : BunitContext
{
    private static PreflightStageDto Blocked(string stageName, string? pipelineName, int depth) => new()
    {
        StageName = stageName,
        Resolved = false,
        Target = "prod-web",
        Reason = "no online matching agent",
        Depth = depth,
        PipelineName = pipelineName
    };

    private IRenderedComponent<PipelineRunPreflightDialog> Render(params PreflightStageDto[] unresolved)
    {
        BunitTestHelper.RegisterServices(this);
        return Render<PipelineRunPreflightDialog>(parameters => parameters
            .Add(dialog => dialog.Unresolved, unresolved));
    }

    [Fact]
    public void AStageOfATriggeredPipelineIsBadgedWithThatPipelineName()
    {
        var markup = Render(Blocked("Deploy", "aetheus-deploy-prod", depth: 1)).Markup;

        Assert.Contains("aetheus-deploy-prod / Deploy", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AStageOfTheLaunchingPipelineKeepsItsBareName()
    {
        var cut = Render(Blocked("Deploy", pipelineName: null, depth: 0));

        Assert.Contains(cut.FindComponents<OmniBadge>(), badge => badge.Find("span").TextContent.Trim() == "Deploy");
        Assert.DoesNotContain(" / Deploy", cut.Markup, StringComparison.Ordinal);
    }
}
