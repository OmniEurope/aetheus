// SPDX-License-Identifier: EUPL-1.2
using Bunit;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// PLAN-005 lot 2 / D32: the detail is clamped to four lines by CSS, and "Show more" appears only
/// when the browser reports that the clamp hides something (a 12-line message), never for a
/// 2-line one. bUnit has no layout, so the browser's measurement is what the test sets.
/// </summary>
public sealed class NotificationBodyTests : BunitContext
{
    public NotificationBodyTests() => BunitTestHelper.RegisterServices(this);

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void ShowMore_AppearsOnlyWhenTheClampHidesText(bool clamped, int buttons)
    {
        JSInterop.Setup<bool>("Aetheus.isClamped", _ => true).SetResult(clamped);

        var cut = Render<NotificationBody>(parameters => parameters
            .Add(body => body.Summary, "Deployment failed")
            .Add(body => body.Detail, string.Join('\n', Enumerable.Range(1, clamped ? 12 : 2).Select(i => $"line {i}"))));

        cut.WaitForAssertion(() => Assert.Equal(buttons, cut.FindAll(".notification-body-more").Count));
        Assert.Contains("notification-body-text", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLogsLink_IsOnlyThereWhenGiven()
    {
        JSInterop.Setup<bool>("Aetheus.isClamped", _ => true).SetResult(false);

        var cut = Render<NotificationBody>(parameters => parameters
            .Add(body => body.Detail, "x")
            .Add(body => body.LogsHref, "/admin/system-logs?search=abc"));

        Assert.Contains("href=\"/admin/system-logs?search=abc\"", cut.Markup, StringComparison.Ordinal);
    }
}
