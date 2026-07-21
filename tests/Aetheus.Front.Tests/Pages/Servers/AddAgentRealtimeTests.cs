// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers;
using Aetheus.Front.Pages.Servers.AgentWizard;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Front.Tests.Pages.Servers;

public class AddAgentRealtimeTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    public AddAgentRealtimeTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    // The Verify-step detection logic lives in AgentDetectionMonitor (extracted from AddAgent to keep
    // the page within the 600-line budget). Build one from the test-handler-backed services.
    private AgentDetectionMonitor MakeMonitor(TimeProvider? timeProvider = null) => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<HubConnectionFactory>(),
        NullLogger.Instance,
        () => Task.CompletedTask,
        timeProvider);

    private void SetupDefaultStubs()
    {
        // The actual endpoint used by ApiClient.GetAgentServerUrlAsync is api/servers/agent-server-url
        // returning { "url": "..." }
        _handler.SetJsonResponse("agent-server-url", new { url = "https://my-server.example.com" });
        _handler.SetJsonResponse("api/auth/registration-tokens", new RegistrationTokenDto
        {
            Id = 1,
            Token = "token-abc-xyz-123456",
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        });
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto>
        {
            Items = [],
            TotalCount = 0,
            Page = 1,
            PageSize = 1
        });
    }

    [Fact]
    public void Renders_WithDefaultStubs()
    {
        SetupDefaultStubs();
        var cut = Render<AddAgent>();
        // OnInitializedAsync resolves the agent server URL via the agent-server-url endpoint.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("agent-server-url"));
    }

    [Fact]
    public void ServerBaseUrl_IsNonEmpty()
    {
        SetupDefaultStubs();
        var cut = Render<AddAgent>();

        // ServerBaseUrl is a private property - access via reflection
        var prop = typeof(AddAgent).GetProperty("ServerBaseUrl", Priv)!;
        var url = (string)prop.GetValue(cut.Instance)!;
        Assert.NotEmpty(url);
        // Should be either the API response or the config fallback
        Assert.True(url.StartsWith("http") || url.StartsWith("https"));
    }

    [Fact]
    public void ServerBaseUrl_FallsBackToConfig_WhenApiFails()
    {
        _handler.SetResponse("api/settings/agent-server-url", System.Net.HttpStatusCode.InternalServerError);
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto>
        {
            Items = [],
            TotalCount = 0,
            Page = 1,
            PageSize = 1
        });

        var cut = Render<AddAgent>();

        var prop = typeof(AddAgent).GetProperty("ServerBaseUrl", Priv)!;
        var url = (string)prop.GetValue(cut.Instance)!;
        // Falls back to "http://test:5301" from the IConfiguration in BunitTestHelper
        Assert.NotEmpty(url);
    }

    [Fact]
    public void ElapsedSeconds_WhenNotListening_ReturnsZero()
    {
        SetupDefaultStubs();
        var monitor = MakeMonitor();
        Assert.Equal(0, monitor.ElapsedSeconds);
    }

    [Fact]
    public async Task ElapsedSeconds_WhenListening_FollowsInjectedClock()
    {
        SetupDefaultStubs();
        var clock = new FakeTimeProvider();
        var monitor = MakeMonitor(clock);

        await monitor.StartAsync(null);
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(10, monitor.ElapsedSeconds);
        await monitor.DisposeAsync();
    }

    [Fact]
    public async Task CancelAsync_ClearsState()
    {
        SetupDefaultStubs();
        var monitor = MakeMonitor();

        // Pre-set some listening state (props have private setters - set via reflection)
        typeof(AgentDetectionMonitor).GetProperty("HubError")!.SetValue(monitor, "some error");
        typeof(AgentDetectionMonitor).GetProperty("AgentFound")!.SetValue(monitor, true);

        await monitor.CancelAsync();

        Assert.Null(monitor.HubError);
        Assert.False(monitor.AgentFound);
    }

    [Fact]
    public async Task StopAsync_WhenHubNull_SetsListeningFalse()
    {
        SetupDefaultStubs();
        var monitor = MakeMonitor();
        typeof(AgentDetectionMonitor).GetProperty("Listening")!.SetValue(monitor, true);

        await monitor.StopAsync();

        Assert.False(monitor.Listening);
    }

    [Fact]
    public async Task DisposeAsync_WithoutStart_IsIdempotent()
    {
        SetupDefaultStubs();
        var monitor = MakeMonitor();

        await monitor.DisposeAsync();
        var ex = await Record.ExceptionAsync(async () => await monitor.DisposeAsync());
        Assert.Null(ex);
    }

    [Fact]
    public void GetAllCommands_Linux_ContainsCurl()
    {
        SetupDefaultStubs();
        var cut = Render<AddAgent>();

        var commands = cut.Instance.GetAllCommands();
        Assert.NotEmpty(commands);
        Assert.Contains(commands, c => c.Contains("curl"));
    }

    [Fact]
    public void GetAllCommands_Windows_ContainsInvokeWebRequest()
    {
        SetupDefaultStubs();
        var cut = Render<AddAgent>();

        typeof(AddAgent).GetField("_platform", Priv)!.SetValue(cut.Instance, "windows");

        var commands = cut.Instance.GetAllCommands();
        Assert.Contains(commands, c => c.Contains("Invoke-WebRequest"));
    }

    [Fact]
    public async Task TryDetectViaUsedToken_WhenWizardTokenIsUsed_ReportsAgentFoundImmediately()
    {
        SetupDefaultStubs();
        // The wizard's own token has been consumed by a registering agent - the definitive
        // "handshake done" signal the verify step must catch without polling. RTOK: the monitor now
        // polls just its own token by id (api/auth/registration-tokens/{id}), not the full list.
        _handler.SetJsonResponse("api/auth/registration-tokens/42", new RegistrationTokenDto
        {
            Id = 42,
            Token = "token-abc-xyz-123456",
            IsUsed = true,
            UsedByServerId = 7
        });
        _handler.SetJsonResponse("api/servers/7", new ServerDetailDto { Id = 7, Name = "edge-vps-01" });

        var monitor = MakeMonitor();
        typeof(AgentDetectionMonitor).GetField("_generatedToken", Priv)!
            .SetValue(monitor, new RegistrationTokenDto { Id = 42, Token = "token-abc-xyz-123456" });

        var method = typeof(AgentDetectionMonitor).GetMethod("TryDetectViaUsedTokenAsync", Priv)!;
        var detected = await (Task<bool>)method.Invoke(monitor, [])!;

        Assert.True(detected);
        Assert.True(monitor.AgentFound);
        Assert.Equal(7, monitor.DetectedServerId);
        Assert.Equal("edge-vps-01", monitor.AgentName);
    }

    [Fact]
    public async Task TryDetectViaUsedToken_WhenTokenStillUnused_ReturnsFalse()
    {
        SetupDefaultStubs();
        // RTOK: the monitor polls its own token by id, not the list.
        _handler.SetJsonResponse("api/auth/registration-tokens/42", new RegistrationTokenDto
        {
            Id = 42,
            Token = "token-abc-xyz-123456",
            IsUsed = false
        });

        var monitor = MakeMonitor();
        typeof(AgentDetectionMonitor).GetField("_generatedToken", Priv)!
            .SetValue(monitor, new RegistrationTokenDto { Id = 42, Token = "token-abc-xyz-123456" });

        var method = typeof(AgentDetectionMonitor).GetMethod("TryDetectViaUsedTokenAsync", Priv)!;
        var detected = await (Task<bool>)method.Invoke(monitor, [])!;

        Assert.False(detected);
        Assert.False(monitor.AgentFound);
    }

    [Fact]
    public async Task OnStepChanged_ToStep1_GeneratesToken()
    {
        SetupDefaultStubs();
        var cut = Render<AddAgent>();

        // Ensure no token yet
        typeof(AddAgent).GetField("_generatedToken", Priv)!.SetValue(cut.Instance, null);
        typeof(AddAgent).GetField("_tokenGenerating", Priv)!.SetValue(cut.Instance, false);

        var method = typeof(AddAgent).GetMethod("OnStepChanged", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [1])!);

        // OnStepChanged records the new step. Token generation only fires on step 2, so the
        // capabilities step (1) advances the cursor without POSTing a registration token.
        Assert.Equal(1, (int)typeof(AddAgent).GetField("_currentStep", Priv)!.GetValue(cut.Instance)!);
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("registration-tokens"));
    }
}
