// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Servers.ServerDetailSections;
using PipelinesComp = Aetheus.Front.Components.Shared.PipelineHelper;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// Tests for internal/public static utility methods that return non-badge values
/// (cron validation, run badge delegation, etc.).
/// </summary>
public class StaticUtilityMethodTests
{
    // --- ServerCronSection.ValidateCronExpression ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateCronExpression_NullOrWhitespace_Invalid(string? expr)
    {
        var (isValid, nextRun, error) = ServerCronSection.ValidateCronExpression(expr);
        Assert.False(isValid);
        Assert.Null(nextRun);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateCronExpression_ValidStandard_ReturnsValid()
    {
        var (isValid, nextRun, error) = ServerCronSection.ValidateCronExpression("*/5 * * * *");
        Assert.True(isValid);
        Assert.NotNull(nextRun);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateCronExpression_SixFieldWithSeconds_Rejected()
    {
        // CR5F: /etc/cron.d is strictly 5-field, so the preview no longer accepts a 6-field schedule.
        var (isValid, nextRun, error) = ServerCronSection.ValidateCronExpression("0 */5 * * * *");
        Assert.False(isValid);
        Assert.Null(nextRun);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateCronExpression_Invalid_ReturnsError()
    {
        var (isValid, nextRun, error) = ServerCronSection.ValidateCronExpression("not-a-cron");
        Assert.False(isValid);
        Assert.Null(nextRun);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("0 0 * * *")]
    [InlineData("0 12 * * MON-FRI")]
    [InlineData("0 0 1 * *")]
    public void ValidateCronExpression_VariousValid_AllValid(string expr)
    {
        var (isValid, _, error) = ServerCronSection.ValidateCronExpression(expr);
        Assert.True(isValid);
        Assert.Null(error);
    }

    // --- PipelinesComp.GetRunBadge (delegates to PipelineHelper) ---

    [Theory]
    [InlineData(PipelineStatus.Success, OmniTone.Success)]
    [InlineData(PipelineStatus.Failed, OmniTone.Danger)]
    [InlineData(PipelineStatus.Running, OmniTone.Accent)]
    // PLAN-003 D13: amber is now Partial (finished with a swallowed failure); a cancelled run is a
    // non-event, so it takes the neutral grey.
    [InlineData(PipelineStatus.Cancelled, OmniTone.Info)]
    [InlineData(PipelineStatus.Partial, OmniTone.Warning)]
    [InlineData(PipelineStatus.Pending, OmniTone.Neutral)]
    public void Pipelines_GetRunBadge_ReturnsExpected(PipelineStatus status, OmniTone expected)
    {
        Assert.Equal(expected, PipelinesComp.GetRunBadge(status));
    }
}
