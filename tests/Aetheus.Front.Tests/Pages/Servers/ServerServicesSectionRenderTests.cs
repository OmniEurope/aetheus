// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Front.Tests.TestDoubles;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Render and method coverage for ServerServicesSection - focuses on uncovered branches:
/// GetStatusBadgeStyle variants, HandleTaskCompleted, AppendLog, CloseLogsAsync,
/// OnRowRender, OpenServiceModule navigation, log buffer overflow.
/// </summary>
public class ServerServicesSectionRenderTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;

    private readonly BunitTestHelper.TestHandler _handler;

    public ServerServicesSectionRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse("api/servers/21/services/action", new { taskId = 101 });
        _handler.SetJsonResponse("api/servers/21/services/install", new { taskId = 102 });
        _handler.SetJsonResponse("api/servers/21/services/uninstall", new { taskId = 103 });
        _handler.SetJsonResponse("api/servers/21/services/logs", new { taskId = 42 });
    }

    private static ServerDetailDto MakeServer(params ServiceInfoDto[] services) => new()
    {
        Id = 21,
        Name = "svc-render-srv",
        Hostname = "10.0.0.21",
        Type = ServerType.Normal,
        Status = ServerStatus.Online,
        Tags = [],
        Services = services.ToList()
    };

    private static ServerDetailDto MakeServer(bool packageMgmt, params ServiceInfoDto[] services) => new()
    {
        Id = 21,
        Name = "svc-render-srv",
        Hostname = "10.0.0.21",
        Type = ServerType.Normal,
        Status = ServerStatus.Online,
        // Both capabilities ship together via the server-management module; mirror that here so the
        // "enabled" case leaves no disabled-tooltip (the teamspeak Install tooltip reuses the same key).
        Tags = [],
        Services = services.ToList(),
        PackageManagementAvailable = packageMgmt,
        TeamspeakSetupAvailable = packageMgmt
    };

    private static ServiceInfoDto Svc(string name, bool installed = true, bool manageable = true,
        bool running = true, string status = "active", ServiceType type = ServiceType.Systemd) => new()
        {
            Name = name,
            Status = status,
            IsRunning = running,
            IsInstalled = installed,
            IsManageable = manageable,
            Type = type
        };

    private IRenderedComponent<ServerServicesSection> RenderSection(ServerDetailDto? server = null)
    {
        var s = server ?? MakeServer();
        return Render<ServerServicesSection>(p => p
            .Add(x => x.Server, s)
            .Add(x => x.ServerId, s.Id));
    }

    // ── GetStatusBadgeStyle ──────────────────────────────────────────────────

    [Theory]
    [InlineData("active", true, BadgeStyle.Success)]
    [InlineData("scheduled", false, BadgeStyle.Info)]
    [InlineData("idle", false, BadgeStyle.Light)]
    [InlineData("dead", false, BadgeStyle.Danger)]
    [InlineData("failed", false, BadgeStyle.Danger)]
    public void GetStatusBadgeStyle_AllVariants(string status, bool isRunning, BadgeStyle expected)
    {
        var result = ServerServicesSection.GetStatusBadgeStyle(status, isRunning);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetStatusBadgeStyle_Running_AlwaysSuccess()
    {
        Assert.Equal(BadgeStyle.Success, ServerServicesSection.GetStatusBadgeStyle("scheduled", true));
    }

    // ── Render with full service data ────────────────────────────────────────

    [Fact]
    public void Renders_WithDockerRunning_ShowsDockerName()
    {
        var cut = RenderSection(MakeServer(Svc("docker"), Svc("apache2")));
        Assert.Contains("docker", cut.Markup);
    }

    [Fact]
    public void Renders_WithNotInstalledService_IncludedInModuleTab()
    {
        var cut = RenderSection(MakeServer(Svc("certbot", installed: false)));
        // Not installed services should appear in the module-not-installed list
        Assert.Contains("certbot", cut.Markup);
    }

    [Fact]
    public void Renders_WindowsServiceType_FormattedCorrectly()
    {
        Assert.Equal("Windows", ServerServicesSection.FormatType(ServiceType.WindowsService));
    }

    [Fact]
    public void Renders_UnknownServiceType_FallsBackToToString()
    {
        var unknown = (ServiceType)999;
        var result = ServerServicesSection.FormatType(unknown);
        Assert.Equal("999", result);
    }

    [Fact]
    public void Renders_SystemServices_ShowsSystemCount()
    {
        var cut = RenderSection(MakeServer(
            Svc("systemd-journald", manageable: false),
            Svc("docker")));
        Assert.Equal(1, cut.Instance.SystemCount);
    }

    // ── Package-manage capability gate ───────────────────────────────────────

    [Fact]
    public void Renders_PackageMgmtDisabled_ShowsCapabilityBannerAndGatesInstall()
    {
        // Default agent posture (no server-management): the capability is OFF, so the section must warn
        // and NOT present install/uninstall as live actions (they would always 400 server-side).
        var cut = RenderSection(MakeServer(packageMgmt: false, Svc("certbot", installed: false)));
        Assert.False(cut.Instance.CanManagePackages);
        // The stub localizer echoes the key, so the banner/disabled-tooltip text is the key itself.
        Assert.Contains("ServiceInstallCapabilityDisabled", cut.Markup);
        // The notice + live heartbeat render as two tiles: the warning-accented card and the
        // heartbeat-pulse tile beside it (the services tables stay tables).
        Assert.Contains("capability-warning-tile", cut.Markup);
        Assert.Contains("heartbeat-pulse", cut.Markup);
    }

    [Fact]
    public void Renders_PackageMgmtEnabled_NoCapabilityBanner()
    {
        var cut = RenderSection(MakeServer(packageMgmt: true, Svc("certbot", installed: false)));
        Assert.True(cut.Instance.CanManagePackages);
        Assert.DoesNotContain("ServiceInstallCapabilityDisabled", cut.Markup);
    }

    [Fact]
    public void Renders_OfflineServer_DisablesEveryMutatingServiceAction()
    {
        var server = MakeServer(
            packageMgmt: true,
            Svc("nginx", running: true),
            Svc("certbot", installed: false)) with
        { Status = ServerStatus.Offline };

        var cut = RenderSection(server);

        var mutating = cut.FindAll("button[title='AgentOfflineActionBlocked']").ToList();
        Assert.NotEmpty(mutating);
        Assert.All(mutating, button => Assert.True(button.HasAttribute("disabled")));
        Assert.Contains("AgentOfflineActionBlocked", cut.Markup);
    }

    [Fact]
    public void SuccessfulInstall_OffersImmediateStartPrompt()
    {
        BunitTestHelper.UseImmediateDialogs(this);
        var cut = RenderSection(MakeServer(packageMgmt: true, Svc("nginx", installed: false)));
        cut.Instance.TrackPendingInstall("nginx", 102);

        cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            ServerId = 21,
            TaskId = 102,
            TaskName = "Install - nginx",
            Status = TaskExecutionStatus.Success
        });

        var dialog = (ImmediateDialogService)Services.GetRequiredService<DialogService>();
        cut.WaitForState(() => dialog.OpenCount == 1);
        Assert.Equal("ServiceInstalledTitle", dialog.LastTitle);
        Assert.NotNull(dialog.LastConfirmMessage);
        Assert.Contains("ConfirmStartAfterInstall", dialog.LastConfirmMessage);
    }

    // ── HandleTaskCompleted ──────────────────────────────────────────────────

    [Fact]
    public void HandleTaskCompleted_MatchingBusyService_ClearsBusyService()
    {
        var cut = RenderSection(MakeServer(Svc("nginx")));
        typeof(ServerServicesSection).GetField("_busyService", Priv)!.SetValue(cut.Instance, "nginx");
        typeof(ServerServicesSection).GetField("_busyTaskId", Priv)!.SetValue(cut.Instance, 1);

        cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            ServerId = 21,
            TaskId = 1,
            TaskName = "Start - nginx",
            Status = TaskExecutionStatus.Success
        });

        var busy = (string?)typeof(ServerServicesSection).GetField("_busyService", Priv)!.GetValue(cut.Instance);
        Assert.Null(busy);
    }

    [Fact]
    public void HandleTaskCompleted_MatchingBusyService_FailedStatus_ClearsBusyService()
    {
        // The failed-task branch (toast.Error path) must still release the row spinner instead of
        // leaving it busy forever - the real-time fix has to surface failure, not swallow it.
        var cut = RenderSection(MakeServer(Svc("nginx")));
        typeof(ServerServicesSection).GetField("_busyService", Priv)!.SetValue(cut.Instance, "nginx");
        typeof(ServerServicesSection).GetField("_busyTaskId", Priv)!.SetValue(cut.Instance, 7);

        cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            ServerId = 21,
            TaskId = 7,
            TaskName = "Install - nginx",
            Status = TaskExecutionStatus.Failed,
            ExitCode = 100
        });

        var busy = (string?)typeof(ServerServicesSection).GetField("_busyService", Priv)!.GetValue(cut.Instance);
        Assert.Null(busy);
    }

    [Fact]
    public void HandleTaskCompleted_NonMatchingBusyService_KeepsBusyService()
    {
        var cut = RenderSection(MakeServer(Svc("nginx")));
        typeof(ServerServicesSection).GetField("_busyService", Priv)!.SetValue(cut.Instance, "nginx");
        typeof(ServerServicesSection).GetField("_busyTaskId", Priv)!.SetValue(cut.Instance, 1);

        // A different task id (an unrelated operation completing first) must NOT clear our busy state.
        cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            ServerId = 21,
            TaskId = 2,
            TaskName = "Start - apache2",
            Status = TaskExecutionStatus.Success
        });

        var busy = (string?)typeof(ServerServicesSection).GetField("_busyService", Priv)!.GetValue(cut.Instance);
        Assert.Equal("nginx", busy);
    }

    [Fact]
    public void HandleTaskCompleted_PreviousServer_DoesNotMutateCurrentState()
    {
        var cut = RenderSection(MakeServer(Svc("nginx")));
        typeof(ServerServicesSection).GetField("_busyService", Priv)!.SetValue(cut.Instance, "nginx");
        typeof(ServerServicesSection).GetField("_busyTaskId", Priv)!.SetValue(cut.Instance, 1);

        cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            ServerId = 22,
            TaskId = 1,
            TaskName = "Start - nginx",
            Status = TaskExecutionStatus.Success
        });

        Assert.Equal("nginx", typeof(ServerServicesSection)
            .GetField("_busyService", Priv)!.GetValue(cut.Instance));
    }

    [Fact]
    public void ServerChange_ClearsBusyAndLogState()
    {
        var cut = RenderSection(MakeServer(Svc("nginx")));
        typeof(ServerServicesSection).GetField("_busyService", Priv)!.SetValue(cut.Instance, "nginx");
        typeof(ServerServicesSection).GetField("_busyTaskId", Priv)!.SetValue(cut.Instance, 12);
        typeof(ServerServicesSection).GetField("_currentLogTaskId", Priv)!.SetValue(cut.Instance, 44);
        typeof(ServerServicesSection).GetField("_logsVisible", Priv)!.SetValue(cut.Instance, true);

        var next = MakeServer(Svc("apache2")) with { Id = 22 };
        cut.Render(parameters => parameters
            .Add(component => component.Server, next)
            .Add(component => component.ServerId, 22));

        Assert.Null(typeof(ServerServicesSection).GetField("_busyService", Priv)!.GetValue(cut.Instance));
        Assert.Null(typeof(ServerServicesSection).GetField("_busyTaskId", Priv)!.GetValue(cut.Instance));
        Assert.Null(typeof(ServerServicesSection).GetField("_currentLogTaskId", Priv)!.GetValue(cut.Instance));
        Assert.False((bool)typeof(ServerServicesSection).GetField("_logsVisible", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task StartLogStreamAsync_WaitsForExistingRestartToFinish()
    {
        var cut = RenderSection(MakeServer(Svc("nginx")));
        _handler.SetResponse(HttpMethod.Post, "api/servers/21/services/logs",
            System.Net.HttpStatusCode.InternalServerError);
        typeof(ServerServicesSection).GetField("_logsServiceName", Priv)!.SetValue(cut.Instance, "nginx");
        var gate = (SemaphoreSlim)typeof(ServerServicesSection)
            .GetField("_logStreamGate", Priv)!.GetValue(cut.Instance)!;
        await gate.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        var method = typeof(ServerServicesSection).GetMethod("StartLogStreamAsync", Priv)!;
        var start = cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.False(start.IsCompleted);
        gate.Release();
        await start;
    }

    [Fact]
    public void HandleTaskCompleted_MatchingLogTaskId_ClearsLoading()
    {
        var cut = RenderSection();
        typeof(ServerServicesSection).GetField("_currentLogTaskId", Priv)!.SetValue(cut.Instance, 99);
        typeof(ServerServicesSection).GetField("_logsLoading", Priv)!.SetValue(cut.Instance, true);

        cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            ServerId = 21,
            TaskId = 99,
            TaskName = "Logs task",
            Output = "log output here",
            Status = TaskExecutionStatus.Success
        });

        Assert.False((bool)typeof(ServerServicesSection).GetField("_logsLoading", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public void HandleTaskCompleted_MatchingLogTaskId_UsesOutput_WhenLogsEmpty()
    {
        var cut = RenderSection();
        typeof(ServerServicesSection).GetField("_currentLogTaskId", Priv)!.SetValue(cut.Instance, 55);
        typeof(ServerServicesSection).GetField("_logsLoading", Priv)!.SetValue(cut.Instance, true);
        typeof(ServerServicesSection).GetField("_logsContent", Priv)!.SetValue(cut.Instance, "");

        cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            ServerId = 21,
            TaskId = 55,
            TaskName = "log task",
            Output = "actual log output",
            Status = TaskExecutionStatus.Success
        });

        var content = (string)typeof(ServerServicesSection).GetField("_logsContent", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal("actual log output", content);
    }

    // ── AppendLog ────────────────────────────────────────────────────────────

    [Fact]
    public void AppendLog_NullMessage_ProducesNothing()
    {
        var cut = RenderSection();
        var method = typeof(ServerServicesSection).GetMethod("AppendLog", Priv)!;
        method.Invoke(cut.Instance, [null]);
        var content = (string)typeof(ServerServicesSection).GetField("_logsContent", Priv)!.GetValue(cut.Instance)!;
        Assert.Empty(content);
    }

    [Fact]
    public void AppendLog_Message_AppendsToBuffer()
    {
        var cut = RenderSection();
        var method = typeof(ServerServicesSection).GetMethod("AppendLog", Priv)!;
        method.Invoke(cut.Instance, ["hello world"]);
        var content = (string)typeof(ServerServicesSection).GetField("_logsContent", Priv)!.GetValue(cut.Instance)!;
        Assert.Contains("hello world", content);
        Assert.Equal(1, (int)typeof(ServerServicesSection).GetField("_logLineCount", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public void AppendLog_MaxLinesExceeded_TruncatesAndContinues()
    {
        var cut = RenderSection();
        // Set line count to max
        typeof(ServerServicesSection).GetField("_logLineCount", Priv)!.SetValue(cut.Instance, 2000);
        var method = typeof(ServerServicesSection).GetMethod("AppendLog", Priv)!;
        method.Invoke(cut.Instance, ["overflow line"]);
        var content = (string)typeof(ServerServicesSection).GetField("_logsContent", Priv)!.GetValue(cut.Instance)!;
        Assert.Contains("truncated", content);
        Assert.Contains("overflow line", content);
    }

    // ── CloseLogsAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task CloseLogsAsync_ClearsLogsState()
    {
        var cut = RenderSection();
        typeof(ServerServicesSection).GetField("_logsVisible", Priv)!.SetValue(cut.Instance, true);
        typeof(ServerServicesSection).GetField("_logsLoading", Priv)!.SetValue(cut.Instance, true);

        var method = typeof(ServerServicesSection).GetMethod("CloseLogsAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.False((bool)typeof(ServerServicesSection).GetField("_logsVisible", Priv)!.GetValue(cut.Instance)!);
        Assert.False((bool)typeof(ServerServicesSection).GetField("_logsLoading", Priv)!.GetValue(cut.Instance)!);
        Assert.Empty((string)typeof(ServerServicesSection).GetField("_logsContent", Priv)!.GetValue(cut.Instance)!);
    }

    // ── OnRowRender ──────────────────────────────────────────────────────────

    // Row greying moved to the reusable ManageableServiceGrid (OnRowRenderInternal, private static).
    [Fact]
    public void OnRowRender_NotInstalled_AddsColorClass()
    {
        var method = typeof(ManageableServiceGrid).GetMethod("OnRowRenderInternal", PrivStatic)!;
        // RowRenderEventArgs<T>.Data is read-only - construct via Activator and set via reflection
        var args = new RowRenderEventArgs<ServiceInfoDto>();
        typeof(RowRenderEventArgs<ServiceInfoDto>)
            .GetProperty("Data")!
            .SetValue(args, new ServiceInfoDto { Name = "nginx", IsInstalled = false });
        method.Invoke(null, [args]);
        Assert.Contains("rz-color-secondary", args.Attributes["class"].ToString());
    }

    [Fact]
    public void OnRowRender_Installed_DoesNotAddColorClass()
    {
        var method = typeof(ManageableServiceGrid).GetMethod("OnRowRenderInternal", PrivStatic)!;
        var args = new RowRenderEventArgs<ServiceInfoDto>();
        typeof(RowRenderEventArgs<ServiceInfoDto>)
            .GetProperty("Data")!
            .SetValue(args, new ServiceInfoDto { Name = "nginx", IsInstalled = true });
        method.Invoke(null, [args]);
        Assert.False(args.Attributes.ContainsKey("class"));
    }

    // ── DisposeAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_NoHub_DoesNotThrow()
    {
        var cut = RenderSection();
        await cut.Instance.DisposeAsync();
        // Disposal is idempotent: a second dispose after the first must not throw either
        // (no hub connection was ever established, so there is nothing to tear down twice).
        var ex = await Record.ExceptionAsync(async () => await cut.Instance.DisposeAsync());
        Assert.Null(ex);
    }

    // ── OnLogLinesChanged / OnFollowChangedAsync ─────────────────────────────

    [Fact]
    public async Task OnLogLinesChanged_UpdatesLinesAndRestartsLogStream()
    {
        var cut = RenderSection();
        // A live log pane is required for the restart to re-request logs (guard: _logsServiceName).
        typeof(ServerServicesSection).GetField("_logsServiceName", Priv)!.SetValue(cut.Instance, "nginx");

        var method = typeof(ServerServicesSection).GetMethod("OnLogLinesChanged", Priv)!;
        // The re-stream reaches the log-hub start (which fails under the test hub); the logs POST is
        // emitted before that, so tolerate the hub failure and assert the observable request.
        var exception = await Record.ExceptionAsync(
            () => cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [200])!));

        Assert.Null(exception);
        Assert.Equal(200, (int)typeof(ServerServicesSection).GetField("_logLines", Priv)!.GetValue(cut.Instance)!);
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/servers/21/services/logs"));
    }

    [Fact]
    public async Task OnFollowChangedAsync_UpdatesFollowAndRestartsLogStream()
    {
        var cut = RenderSection();
        typeof(ServerServicesSection).GetField("_logsServiceName", Priv)!.SetValue(cut.Instance, "nginx");

        var method = typeof(ServerServicesSection).GetMethod("OnFollowChangedAsync", Priv)!;
        var exception = await Record.ExceptionAsync(
            () => cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [true])!));

        Assert.Null(exception);
        Assert.True(cut.Instance.IsFollowingLogs);
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/servers/21/services/logs"));
    }

    [Fact]
    public async Task FollowLogsToggle_ThroughRenderedControl_RestartsStream()
    {
        var cut = RenderSection();
        await cut.InvokeAsync(() => cut.Instance.ViewServiceLogsAsync("nginx"));
        cut.Render();
        _handler.Requests.Clear();

        var exception = Record.Exception(
            () => cut.Find(".labeled-toggle-native-input").Change(true));

        Assert.Null(exception);
        // Change() dispatches the handler without awaiting it, unlike the sibling test that invokes
        // OnFollowChangedAsync directly. Asserting immediately raced the continuation and failed only
        // under full-suite load; waiting keeps the same discriminating power, because a toggle that
        // never restarts the stream still never satisfies these assertions.
        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Instance.IsFollowingLogs);
            Assert.Contains(_handler.Requests,
                r => r.Method == "POST" && r.Url.Contains("api/servers/21/services/logs"));
        });
    }
}
