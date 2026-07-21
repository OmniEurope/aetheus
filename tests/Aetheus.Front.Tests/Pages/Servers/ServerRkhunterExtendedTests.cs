// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerRkhunterExtendedTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly BunitTestHelper.TestHandler _handler;

    public ServerRkhunterExtendedTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private void SetupWarnings(List<RkhunterWarningDto>? warnings = null)
    {
        _handler.SetJsonResponse("api/servers/50/rkhunter/warnings", warnings ?? []);
        // GetRkhunterScanHistoryAsync fetches api/servers/{id}/rkhunter/history (not scan-history)
        _handler.SetJsonResponse("api/servers/50/rkhunter/history", new List<RkhunterScanResultDto>());
        _handler.SetJsonResponse("api/servers/50/rkhunter/action", true);
        _handler.SetJsonResponse("api/servers/50/rkhunter/schedule", true);
    }

    private IRenderedComponent<ServerRkhunterSection> RenderInstalled(RkhunterDataDto? rk = null)
    {
        SetupWarnings();
        var data = rk ?? new RkhunterDataDto
        {
            IsInstalled = true,
            Version = "1.4.6",
            WarningCount = 0,
            ScanScheduleCron = "0 3 * * *"
        };
        return Render<ServerRkhunterSection>(p => p
            .Add(x => x.ServerId, 50)
            .Add(x => x.Rk, data));
    }

    [Fact]
    public void Renders_InstalledState_ShowsRkhunterHeading()
    {
        var cut = RenderInstalled();
        Assert.Contains("RKHunter", cut.Markup);
    }

    [Fact]
    public void Renders_InstalledState_ShowsVersion()
    {
        var cut = RenderInstalled();
        Assert.Contains("1.4.6", cut.Markup);
    }

    [Fact]
    public void Renders_NotInstalled_ShowsRkhunterHeading()
    {
        _handler.SetJsonResponse("api/servers/50/rkhunter/warnings", new List<RkhunterWarningDto>());
        var rk = new RkhunterDataDto { IsInstalled = false };
        var cut = Render<ServerRkhunterSection>(p => p
            .Add(x => x.ServerId, 50)
            .Add(x => x.Rk, rk));
        Assert.Contains("RKHunter", cut.Markup);
    }

    [Fact]
    public void Renders_WithWarnings_ShowsWarningCount()
    {
        SetupWarnings(
        [
            new RkhunterWarningDto { Id = 1, Category = "rootkit", Detail = "suspicious file", Severity = "warning", FoundAt = DateTime.UtcNow }
        ]);
        var rk = new RkhunterDataDto { IsInstalled = true, Version = "1.4.6", WarningCount = 1 };
        var cut = Render<ServerRkhunterSection>(p => p
            .Add(x => x.ServerId, 50)
            .Add(x => x.Rk, rk));
        Assert.Contains("1", cut.Markup);
    }

    [Fact]
    public void SameServerRerender_PreservesLocalScheduleEdit_AndServerChangeResetsIt()
    {
        var cut = RenderInstalled(new RkhunterDataDto
        {
            IsInstalled = true,
            ScanScheduleCron = "0 3 * * *"
        });
        var change = typeof(ServerRkhunterSection).GetMethod("OnScheduleChanged", Priv)!;
        change.Invoke(cut.Instance, ["0 4 * * *"]);

        cut.Render(parameters => parameters
            .Add(component => component.ServerId, 50)
            .Add(component => component.Rk, new RkhunterDataDto
            {
                IsInstalled = true,
                ScanScheduleCron = "0 5 * * *"
            }));
        Assert.Equal("0 4 * * *", typeof(ServerRkhunterSection)
            .GetField("_scheduleCron", Priv)!.GetValue(cut.Instance));

        _handler.SetJsonResponse("api/servers/51/rkhunter/warnings", new List<RkhunterWarningDto>());
        cut.Render(parameters => parameters
            .Add(component => component.ServerId, 51)
            .Add(component => component.Rk, new RkhunterDataDto
            {
                IsInstalled = true,
                ScanScheduleCron = "30 2 * * *"
            }));
        Assert.Equal("30 2 * * *", typeof(ServerRkhunterSection)
            .GetField("_scheduleCron", Priv)!.GetValue(cut.Instance));
    }

    [Fact]
    public async Task ExecuteRkhunterActionAsync_Completes_WithoutThrowing()
    {
        var cut = RenderInstalled();

        var method = typeof(ServerRkhunterSection).GetMethod("ExecuteActionAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [RkhunterAction.RunScan])!;

        var running = (bool)typeof(ServerRkhunterSection).GetField("_actionRunning", Priv)!.GetValue(cut.Instance)!;
        Assert.False(running);
    }

    [Fact]
    public async Task SaveScheduleAsync_SendsRequest()
    {
        var cut = RenderInstalled();

        typeof(ServerRkhunterSection).GetField("_scheduleCron", Priv)!.SetValue(cut.Instance, "0 4 * * *");
        var method = typeof(ServerRkhunterSection).GetMethod("SaveScheduleAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.False((bool)typeof(ServerRkhunterSection).GetField("_savingSchedule", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task SaveScheduleAsync_EmptyCron_SendsNull()
    {
        var cut = RenderInstalled();

        typeof(ServerRkhunterSection).GetField("_scheduleCron", Priv)!.SetValue(cut.Instance, "");
        var method = typeof(ServerRkhunterSection).GetMethod("SaveScheduleAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.False((bool)typeof(ServerRkhunterSection).GetField("_savingSchedule", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task HandleTaskCompletedAsync_RkhunterTask_RefreshesData()
    {
        var cut = RenderInstalled();

        var notification = new TaskCompletedNotification { TaskName = "Run RKHunter Scan", ServerId = 50 };
        await cut.Instance.HandleTaskCompletedAsync(notification);

        // A RKHunter task triggers a refresh: scan-history is (re)fetched (it is not loaded on init).
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/servers/50/rkhunter/history"));
    }

    [Fact]
    public async Task HandleTaskCompletedAsync_UnrelatedTask_IgnoresIt()
    {
        var cut = RenderInstalled();

        var notification = new TaskCompletedNotification { TaskName = "Deploy App", ServerId = 50 };
        await cut.Instance.HandleTaskCompletedAsync(notification);

        // A non-RKHunter task short-circuits before any refresh - scan-history is never fetched.
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("api/servers/50/rkhunter/history"));
    }

    [Fact]
    public void GetScanStatusIcon_Clean_ReturnsCheckCircle()
    {
        var method = typeof(ServerRkhunterSection).GetMethod("GetScanStatusIcon",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal("check_circle", method.Invoke(null, ["clean"]));
    }

    [Fact]
    public void GetScanBadgeStyle_Warning_ReturnsWarningStyle()
    {
        var method = typeof(ServerRkhunterSection).GetMethod("GetScanBadgeStyle",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.NotNull(method.Invoke(null, ["warning"]));
    }
}
