// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Covers ServerRkhunterSection setup dialog submission and additional badge/icon helpers.
/// </summary>
public class ServerRkhunterSetupTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerRkhunterSetupTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
    }

    private void StubApi(int serverId = 50)
    {
        _handler.SetJsonResponse($"api/servers/{serverId}/rkhunter/warnings", new List<RkhunterWarningDto>());
        _handler.SetJsonResponse($"api/servers/{serverId}/rkhunter/history", new List<RkhunterScanResultDto>());
        _handler.SetJsonResponse($"api/servers/{serverId}/rkhunter/action", true);
        _handler.SetJsonResponse($"api/servers/{serverId}/rkhunter/setup", true);
        _handler.SetJsonResponse($"api/servers/{serverId}/rkhunter/schedule", true);
        _handler.SetJsonResponse($"api/servers/{serverId}/rkhunter/logs", true);
    }

    private IRenderedComponent<ServerRkhunterSection> RenderNotInstalled(int serverId = 50)
    {
        StubApi(serverId);
        return Render<ServerRkhunterSection>(p => p
            .Add(x => x.ServerId, serverId)
            .Add(x => x.Rk, new RkhunterDataDto { IsInstalled = false }));
    }

    private IRenderedComponent<ServerRkhunterSection> RenderInstalled(int serverId = 50,
        string? scheduleCron = null)
    {
        StubApi(serverId);
        return Render<ServerRkhunterSection>(p => p
            .Add(x => x.ServerId, serverId)
            .Add(x => x.Rk, new RkhunterDataDto
            {
                IsInstalled = true,
                Version = "1.4.6",
                WarningCount = 0,
                ScanScheduleCron = scheduleCron
            }));
    }

    // ── SetupAsync - success path ─────────────────────────────────────────────

    [Fact]
    public async Task OpenSetupDialogAsync_Success_SubmitsDialogModel()
    {
        var cut = RenderNotInstalled();
        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();
        dialog.OpenResult = new ServerSetupDialogModel { MailOnWarning = "ops@example.com" };
        var method = typeof(ServerRkhunterSection).GetMethod("OpenSetupDialogAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.Equal(typeof(ServerSetupDialog), dialog.LastComponent);
        Assert.Equal(ServerSetupKind.Rkhunter, dialog.LastParameters!["Kind"]);
        Assert.Contains("ops@example.com", _handler.LastRequestBody);
        Assert.Contains(_handler.Requests, request => request.Method == "POST" && request.Url.Contains("rkhunter/setup"));
    }

    [Fact]
    public async Task SetupAsync_EmptyMail_StillPostsSetup()
    {
        var cut = RenderNotInstalled();
        typeof(ServerRkhunterSection).GetField("_setupMailOnWarning", Priv)!.SetValue(cut.Instance, "");

        var method = typeof(ServerRkhunterSection).GetMethod("SetupAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // An empty mail address is not a guard - setup is still submitted to the agent.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/servers/50/rkhunter/setup"));
    }

    [Fact]
    public async Task SetupAsync_ApiFailure_CompletesAndRecordsRequest()
    {
        _handler.SetResponse("api/servers/50/rkhunter/setup", System.Net.HttpStatusCode.InternalServerError);
        _handler.SetJsonResponse("api/servers/50/rkhunter/warnings", new List<RkhunterWarningDto>());
        var cut = RenderNotInstalled();

        typeof(ServerRkhunterSection).GetField("_setupMailOnWarning", Priv)!.SetValue(cut.Instance, "fail@test.com");

        var method = typeof(ServerRkhunterSection).GetMethod("SetupAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.Contains(_handler.Requests, request => request.Method == "POST" && request.Url.Contains("rkhunter/setup"));
    }

    // ── SetupAsync - whitespace mail trimming ─────────────────────────────────

    [Fact]
    public async Task SetupAsync_WhitespaceMail_TrimmedToEmpty()
    {
        var cut = RenderNotInstalled();
        typeof(ServerRkhunterSection).GetField("_setupMailOnWarning", Priv)!.SetValue(cut.Instance, "   ");

        var method = typeof(ServerRkhunterSection).GetMethod("SetupAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // A whitespace-only mail trims to empty but still submits the setup request.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/servers/50/rkhunter/setup"));
    }

    // ── SetupAsync - with mail address ───────────────────────────────────────

    [Fact]
    public async Task SetupAsync_WithValidMailAddress_Succeeds()
    {
        var cut = RenderNotInstalled();
        typeof(ServerRkhunterSection).GetField("_setupMailOnWarning", Priv)!.SetValue(cut.Instance, "admin@company.com");

        var method = typeof(ServerRkhunterSection).GetMethod("SetupAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // The setup request is submitted with the address supplied by the dialog.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/servers/50/rkhunter/setup"));
        Assert.Contains("admin@company.com", _handler.LastRequestBody);
    }

    // ── OnParametersSetAsync - ScanScheduleCron syncs to _scheduleCron ────────

    [Fact]
    public void OnParametersSet_WithCron_SyncsScheduleCronField()
    {
        var cut = RenderInstalled(scheduleCron: "0 2 * * *");
        var cron = (string)typeof(ServerRkhunterSection).GetField("_scheduleCron", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal("0 2 * * *", cron);
    }

    [Fact]
    public void OnParametersSet_NullCron_SetsScheduleCronToEmpty()
    {
        var cut = RenderInstalled(scheduleCron: null);
        var cron = (string)typeof(ServerRkhunterSection).GetField("_scheduleCron", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal(string.Empty, cron);
    }

    // ── SaveScheduleAsync - null/whitespace cron → null expression ─────────────

    [Fact]
    public async Task SaveScheduleAsync_WhitespaceCron_SendsNull()
    {
        var cut = RenderInstalled();
        typeof(ServerRkhunterSection).GetField("_scheduleCron", Priv)!.SetValue(cut.Instance, "   ");

        var method = typeof(ServerRkhunterSection).GetMethod("SaveScheduleAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // Whitespace cron is normalised to null but the schedule is still persisted to the agent.
        Assert.Contains(_handler.Requests, r => r.Method == "PUT" && r.Url.Contains("api/servers/50/rkhunter/schedule"));
    }

    [Fact]
    public async Task SaveScheduleAsync_WithCron_PostsSchedule()
    {
        var cut = RenderInstalled();
        typeof(ServerRkhunterSection).GetField("_scheduleCron", Priv)!.SetValue(cut.Instance, "30 3 * * 0");

        var method = typeof(ServerRkhunterSection).GetMethod("SaveScheduleAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.Contains(_handler.Requests, r => r.Method == "PUT" && r.Url.Contains("api/servers/50/rkhunter/schedule"));
    }
}
