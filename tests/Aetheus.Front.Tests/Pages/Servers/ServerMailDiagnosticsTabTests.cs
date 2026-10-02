// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>PLAN-005 lots 6 and 7: the diagnostics tab renders the backend checklist, and each server-side
/// check reads the output of the very task it queued once that task completes.</summary>
public sealed class ServerMailDiagnosticsTabTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private readonly ImmediateDialogService _dialog;

    public ServerMailDiagnosticsTabTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
        _dialog = (ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();
        _handler.SetJsonResponse("api/servers/10/mail/diagnostics", new MailDiagnosticsDto
        {
            CheckedAt = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc),
            Items =
            [
                new() { Key = "tls", Verdict = MailCheckVerdict.Mismatch, Detail = "self-signed" },
                new() { Key = "mx:example.com", Verdict = MailCheckVerdict.Ok, Detail = "mail.example.com" }
            ]
        });
    }

    private IRenderedComponent<ServerMailDiagnosticsTab> RenderTab() =>
        Render<ServerMailDiagnosticsTab>(p => p
            .Add(x => x.ServerId, 10)
            .Add(x => x.CanManage, true)
            .Add(x => x.DefaultSender, "admin@example.com"));

    private void StubTaskOutput(int taskId, params string[] lines) =>
        _handler.SetJsonResponse($"api/logs/task/{taskId}",
            lines.Select((line, index) => new TaskLogDto { Id = index + 1, TaskId = taskId, Message = line }).ToList());

    [Fact]
    public void Checklist_ShowsLocalisedLabelsVerdictsAndDetails()
    {
        var cut = RenderTab();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("MailDiagTls", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("MailDetailSelfSigned", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("MailDiagMx", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("MailVerdictMismatch", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ServerCheck_ReadsTheCheckLinesOfItsOwnTask()
    {
        _handler.SetJsonResponse(System.Net.Http.HttpMethod.Post, "api/servers/10/mail/action", new MailTaskQueuedDto { TaskId = 41 });
        StubTaskOutput(41, "CHECK\tpostfix\tok\t", "CHECK\tport-587\tfail\tnot listening");
        var cut = RenderTab();

        cut.FindAll("button").First(b => b.TextContent.Contains("MailRunServerCheck", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(_handler.RequestDetails, r =>
            r.Method == "POST" && r.Url.EndsWith("mail/action", StringComparison.Ordinal)
            && r.Body!.Contains($"\"action\":{(int)MailAction.TestConfig}", StringComparison.Ordinal)));

        // A completion of another task must be ignored.
        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompletedAsync(new TaskCompletedNotification { TaskId = 7, ServerId = 10 }));
        Assert.Empty(cut.Instance.Checks);

        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompletedAsync(
            new TaskCompletedNotification { TaskId = 41, ServerId = 10, Status = TaskExecutionStatus.Failed }));

        Assert.Equal(2, cut.Instance.Checks.Count);
        Assert.False(cut.Instance.Checks.Single(c => c.Component == "port-587").IsOk);
    }

    [Fact]
    public async Task DeliveryTest_ShowsTheDeliveryLineOfTheHelper()
    {
        _dialog.OpenResult = new MailDialogModel { Mode = MailDialogMode.TestDelivery, Source = "admin@example.com", Destination = "bob@example.org" };
        _handler.SetJsonResponse(System.Net.Http.HttpMethod.Post, "api/servers/10/mail/test-delivery", new MailTaskQueuedDto { TaskId = 42 });
        StubTaskOutput(42, "some chatter", "DELIVERY\tsent\t68436587DA\taetheus-test-1-2\tstatus=sent (250 2.0.0 Saved)");
        var cut = RenderTab();

        cut.FindAll("button").First(b => b.TextContent.Contains("MailSendTest", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(_handler.RequestDetails, r =>
            r.Url.EndsWith("mail/test-delivery", StringComparison.Ordinal) && r.Body!.Contains("bob@example.org", StringComparison.Ordinal)));
        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompletedAsync(
            new TaskCompletedNotification { TaskId = 42, ServerId = 10, Status = TaskExecutionStatus.Success }));

        Assert.NotNull(cut.Instance.Delivery);
        Assert.True(cut.Instance.Delivery.IsSent);
        Assert.Contains("MailDeliverySent", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsageMeasurement_IngestsTheCompletedReport()
    {
        _handler.SetJsonResponse(System.Net.Http.HttpMethod.Post, "api/servers/10/mail/quota/refresh", new MailTaskQueuedDto { TaskId = 43 });
        _handler.SetJsonResponse(System.Net.Http.HttpMethod.Post, "api/servers/10/mail/quota/ingest/43", 2);
        var cut = RenderTab();

        cut.FindAll("button").First(b => b.TextContent.Contains("MailMeasureUsage", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(_handler.Requests, r => r.Url.EndsWith("mail/quota/refresh", StringComparison.Ordinal)));
        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompletedAsync(
            new TaskCompletedNotification { TaskId = 43, ServerId = 10, Status = TaskExecutionStatus.Success }));

        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.EndsWith("mail/quota/ingest/43", StringComparison.Ordinal));
    }

    [Fact]
    public async Task R181_NoRefreshButton_TheChecklistFollowsTheServersPushes()
    {
        var cut = RenderTab();
        cut.WaitForAssertion(() => Assert.Contains("MailDetailSelfSigned", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Refresh", StringComparison.Ordinal));
        _handler.SetJsonResponse("api/servers/10/mail/diagnostics", new MailDiagnosticsDto
        {
            CheckedAt = new DateTime(2026, 9, 14, 12, 1, 0, DateTimeKind.Utc),
            Items = [new() { Key = "tls", Verdict = MailCheckVerdict.Ok }]
        });

        // Reading the checklist queues no task, so a completed task may reload it as a heartbeat does.
        await cut.Instance.LiveFeed!.OnHeartbeatAsync(10);
        cut.WaitForAssertion(() => Assert.DoesNotContain("MailDetailSelfSigned", cut.Markup, StringComparison.Ordinal));
        var afterHeartbeat = _handler.Requests.Count(r => r.Url.Contains("mail/diagnostics", StringComparison.Ordinal));

        await cut.Instance.LiveFeed.OnTaskCompletedAsync(new TaskCompletedNotification { TaskId = 5, ServerId = 10 });
        await cut.Instance.LiveFeed.OnHeartbeatAsync(11);

        Assert.Equal(afterHeartbeat + 1,
            _handler.Requests.Count(r => r.Url.Contains("mail/diagnostics", StringComparison.Ordinal)));
    }
}
