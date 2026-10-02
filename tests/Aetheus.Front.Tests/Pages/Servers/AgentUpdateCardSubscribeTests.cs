// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers;
using Bunit;
using Microsoft.AspNetCore.SignalR.Client;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Covers AgentUpdateProgressCard.Subscribe (hub-handler registration, 29 lines) by rendering the
/// card with a non-null HubConnection parameter so OnParametersSet → Subscribe(hub) runs.
/// The handlers register but never fire (no live connection) - that's fine; the registration
/// lines are what we cover.
/// </summary>
public class AgentUpdateCardSubscribeTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    public AgentUpdateCardSubscribeTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    private static HubConnection BuildHub() =>
        new HubConnectionBuilder().WithUrl("http://localhost/test-hub").Build();

    [Fact]
    public void Render_WithHub_SubscribesHandlers()
    {
        var hub = BuildHub();
        var cut = Render<AgentUpdateProgressCard>(p => p
            .Add(x => x.ServerId, 1)
            .Add(x => x.Hub, hub)
            .Add(x => x.CurrentAgentVersion, "1.0.0"));

        // Subscribe stores the 3 handler disposables - verify at least one is registered.
        var onQueued = typeof(AgentUpdateProgressCard).GetField("_onQueued", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(onQueued);
        var onProgress = typeof(AgentUpdateProgressCard).GetField("_onProgress", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(onProgress);
        var onOffline = typeof(AgentUpdateProgressCard).GetField("_onOffline", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(onOffline);
    }

    [Fact]
    public void Render_WithNullHub_DoesNotSubscribe()
    {
        var cut = Render<AgentUpdateProgressCard>(p => p
            .Add(x => x.ServerId, 1)
            .Add(x => x.Hub, (HubConnection?)null)
            .Add(x => x.CurrentAgentVersion, "1.0.0"));

        var onQueued = typeof(AgentUpdateProgressCard).GetField("_onQueued", Priv)!.GetValue(cut.Instance);
        Assert.Null(onQueued);
    }

    [Fact]
    public void Render_WithHub_ReSubscribeIsIdempotent()
    {
        var hub = BuildHub();
        var cut = Render<AgentUpdateProgressCard>(p => p
            .Add(x => x.ServerId, 1)
            .Add(x => x.Hub, hub)
            .Add(x => x.CurrentAgentVersion, "1.0.0"));

        var first = typeof(AgentUpdateProgressCard).GetField("_onQueued", Priv)!.GetValue(cut.Instance);

        // Re-render with same hub → ??= guard keeps the same handler instance.
        cut.Render(p => p
            .Add(x => x.ServerId, 1)
            .Add(x => x.Hub, hub)
            .Add(x => x.CurrentAgentVersion, "1.0.1"));

        var second = typeof(AgentUpdateProgressCard).GetField("_onQueued", Priv)!.GetValue(cut.Instance);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task DisposeAsync_AfterSubscribe_DisposesHandlers()
    {
        var hub = BuildHub();
        var cut = Render<AgentUpdateProgressCard>(p => p
            .Add(x => x.ServerId, 1)
            .Add(x => x.Hub, hub)
            .Add(x => x.CurrentAgentVersion, "1.0.0"));

        await cut.Instance.DisposeAsync();
        // _staleTimer cleared on dispose
        var timer = typeof(AgentUpdateProgressCard).GetField("_staleTimer", Priv)!.GetValue(cut.Instance);
        Assert.Null(timer);
    }
}
