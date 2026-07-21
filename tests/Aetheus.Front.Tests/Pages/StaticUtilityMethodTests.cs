// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.Enums;
using Radzen;
using PipelinesComp = Aetheus.Front.Helpers.PipelineHelper;

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
    [InlineData(PipelineStatus.Success, BadgeStyle.Success)]
    [InlineData(PipelineStatus.Failed, BadgeStyle.Danger)]
    [InlineData(PipelineStatus.Running, BadgeStyle.Info)]
    [InlineData(PipelineStatus.Cancelled, BadgeStyle.Warning)]
    [InlineData(PipelineStatus.Pending, BadgeStyle.Light)]
    public void Pipelines_GetRunBadge_ReturnsExpected(PipelineStatus status, BadgeStyle expected)
    {
        Assert.Equal(expected, PipelinesComp.GetRunBadge(status));
    }
}
