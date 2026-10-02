// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers;
using Aetheus.Front.Components.Servers.AgentWizard;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Deep coverage for the Verify-step real-time detection - countdown timer lifecycle, elapsed-time
/// computation, StopAsync with no hub, initial state - now owned by <see cref="AgentDetectionMonitor"/>
/// (extracted from AddAgent to keep the page within the 600-line budget). Plus AddAgent-level checks
/// (DisposeAsync, GetAllCommands).
/// </summary>
public class AddAgentDeepTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public AddAgentDeepTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private void SetupDefaultStubs()
    {
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

    private AgentDetectionMonitor MakeMonitor(TimeProvider? timeProvider = null, Func<Task>? onChanged = null) => new(
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<HubConnectionFactory>(),
        NullLogger.Instance,
        onChanged ?? (() => Task.CompletedTask),
        timeProvider);

    // ── Test 1: start owns tracked polling/countdown tasks ───────────────────

    [Fact]
    public async Task StartAsync_StartsTrackedLifecycleTasks()
    {
        SetupDefaultStubs();
        var clock = new FakeTimeProvider();
        var monitor = MakeMonitor(clock);

        await monitor.StartAsync(null);

        Assert.True(monitor.Listening);
        Assert.False(((Task)typeof(AgentDetectionMonitor).GetField("_countdownTask", Priv)!
            .GetValue(monitor)!).IsCompleted);
        Assert.False(((Task)typeof(AgentDetectionMonitor).GetField("_pollingTask", Priv)!
            .GetValue(monitor)!).IsCompleted);
        await monitor.DisposeAsync();
    }

    // ── Test 2: stop cancels callbacks and waits for both tasks ──────────────

    [Fact]
    public async Task StopAsync_WaitsForTrackedLifecycleTasks()
    {
        SetupDefaultStubs();
        var clock = new FakeTimeProvider();
        var changes = 0;
        var monitor = MakeMonitor(clock, () => { changes++; return Task.CompletedTask; });

        await monitor.StartAsync(null);
        await monitor.StopAsync();
        var afterStop = changes;
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.False(monitor.Listening);
        Assert.True(((Task)typeof(AgentDetectionMonitor).GetField("_countdownTask", Priv)!
            .GetValue(monitor)!).IsCompleted);
        Assert.True(((Task)typeof(AgentDetectionMonitor).GetField("_pollingTask", Priv)!
            .GetValue(monitor)!).IsCompleted);
        Assert.Equal(afterStop, changes);
    }

    // ── Test 3: MaxSeconds is a positive constant ────────────────────────────

    [Fact]
    public void MaxSeconds_IsPositive()
    {
        Assert.True(AgentDetectionMonitor.MaxSeconds > 0, "MaxSeconds must be a positive timeout bound");
    }

    // ── Test 4: Listening starts false ────────────────────────────────────────

    [Fact]
    public void Listening_InitialState_IsFalse()
    {
        SetupDefaultStubs();
        Assert.False(MakeMonitor().Listening);
    }

    // ── Test 5: AgentFound starts false ──────────────────────────────────────

    [Fact]
    public void AgentFound_InitialState_IsFalse()
    {
        SetupDefaultStubs();
        Assert.False(MakeMonitor().AgentFound);
    }

    // ── Test 6: TimedOut starts false ────────────────────────────────────────

    [Fact]
    public void TimedOut_InitialState_IsFalse()
    {
        SetupDefaultStubs();
        Assert.False(MakeMonitor().TimedOut);
    }

    // ── Test 7: CancelAsync clears hub error and agentFound ──────────────────

    [Fact]
    public async Task CancelAsync_ClearsHubErrorAndAgentFound()
    {
        SetupDefaultStubs();
        var monitor = MakeMonitor();

        typeof(AgentDetectionMonitor).GetProperty("HubError")!.SetValue(monitor, "connection refused");
        typeof(AgentDetectionMonitor).GetProperty("AgentFound")!.SetValue(monitor, true);

        await monitor.CancelAsync();

        Assert.Null(monitor.HubError);
        Assert.False(monitor.AgentFound);
    }

    // ── Test 8: StopAsync with null hub sets Listening false ─────────────────

    [Fact]
    public async Task StopAsync_NullHub_SetsListeningFalse()
    {
        SetupDefaultStubs();
        var monitor = MakeMonitor();
        typeof(AgentDetectionMonitor).GetProperty("Listening")!.SetValue(monitor, true);

        await monitor.StopAsync();

        Assert.False(monitor.Listening);
    }

    // ── Test 9: AddAgent.DisposeAsync does not throw ──────────────────────────

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        SetupDefaultStubs();
        var cut = Render<AddAgent>();

        await cut.Instance.DisposeAsync();
        // Disposal is idempotent: a second DisposeAsync after the first must not throw
        // (timers/hub are already torn down and nulled).
        var ex = await Record.ExceptionAsync(async () => await cut.Instance.DisposeAsync());
        Assert.Null(ex);
    }

    // ── Test 10: no elapsed duration before start ─────────────────────────────

    [Fact]
    public void ElapsedSeconds_InitialState_IsZero()
    {
        SetupDefaultStubs();
        var monitor = MakeMonitor();

        Assert.Equal(0, monitor.ElapsedSeconds);
    }

    // ── Test 11: ElapsedSeconds calculates elapsed time ───────────────────────

    [Fact]
    public async Task ElapsedSeconds_UsesMonotonicTimeProvider()
    {
        SetupDefaultStubs();
        var clock = new FakeTimeProvider();
        var monitor = MakeMonitor(clock);

        await monitor.StartAsync(null);
        clock.Advance(TimeSpan.FromSeconds(15));

        Assert.Equal(15, monitor.ElapsedSeconds);
        await monitor.DisposeAsync();
    }

    // ── Test 12: GetAllCommands returns non-empty for default platform ────────

    [Fact]
    public void GetAllCommands_DefaultPlatform_ReturnsCommands()
    {
        SetupDefaultStubs();
        var cut = Render<AddAgent>();

        var commands = cut.Instance.GetAllCommands();
        Assert.NotEmpty(commands);
    }
}
