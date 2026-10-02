// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers.Sections;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Tests for the thin routing-layer section pages under Components/Servers/Sections/.
/// Each page's OnParametersSetAsync is tested with and without a loader, and the
/// real subscribe/unsubscribe lifecycle against the loader's OnTaskCompleted event
/// is asserted (subscriber count goes 0 → 1 on parameters, back to 0 on dispose).
/// </summary>
public class ServerSectionRoutingTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerSectionRoutingTests() => _handler = BunitTestHelper.RegisterServices(this);

    private ServerDetailLoader CreateLoader()
    {
        return new ServerDetailLoader(
            Services.GetRequiredService<ApiClient>(),
            Services.GetRequiredService<HubConnectionFactory>(),
            Services.GetRequiredService<NavigationManager>(),
            NullLogger<ServerDetailLoader>.Instance);
    }

    /// <summary>Backing-delegate subscriber count of the loader's OnTaskCompleted event.</summary>
    private static int SubscriberCount(ServerDetailLoader loader)
    {
        var del = (Delegate?)typeof(ServerDetailLoader).GetField("OnTaskCompleted", Priv)!.GetValue(loader);
        return del?.GetInvocationList().Length ?? 0;
    }

    /// <summary>
    /// Pre-seeds the loader so EnsureLoadedAsync early-returns (keeps the test off the
    /// failing SignalR hub) while the section's OnParametersSetAsync still runs its subscribe block.
    /// </summary>
    private static void Preload(ServerDetailLoader loader, int id)
    {
        typeof(ServerDetailLoader).GetField("_currentId", Priv)!.SetValue(loader, id);
        typeof(ServerDetailLoader).GetProperty("Server")!.SetValue(loader, new ServerDetailDto { Id = id, Name = $"srv-{id}" });
    }

    private static async Task RunParametersAsync(object component)
    {
        var method = component.GetType().GetMethod("OnParametersSetAsync", Priv)!;
        await (Task)method.Invoke(component, [])!;
    }

    // ── OnParametersSetAsync: Loader is null → no-op, never subscribes ──────────

    [Fact]
    public async Task Rkhunter_OnParametersSetAsync_NullLoader_DoesNotSubscribe()
    {
        var component = new Rkhunter();
        typeof(Rkhunter).GetProperty("Id")!.SetValue(component, 1);
        await RunParametersAsync(component);
        // With no loader, the subscribe block is skipped and _subscribed stays null.
        Assert.Null(typeof(ServerTaskAwareSectionBase).GetField("_subscribedLoader", Priv)!.GetValue(component));
    }

    [Fact]
    public async Task Mail_OnParametersSetAsync_NullLoader_DoesNotSubscribe()
    {
        var component = new Mail();
        typeof(Mail).GetProperty("Id")!.SetValue(component, 2);
        await RunParametersAsync(component);
        Assert.Null(typeof(ServerTaskAwareSectionBase).GetField("_subscribedLoader", Priv)!.GetValue(component));
    }

    [Fact]
    public async Task Services_OnParametersSetAsync_NullLoader_DoesNotSubscribe()
    {
        var component = new Aetheus.Front.Components.Servers.Sections.Services();
        typeof(Aetheus.Front.Components.Servers.Sections.Services).GetProperty("Id")!.SetValue(component, 3);
        await RunParametersAsync(component);
        Assert.Null(typeof(ServerTaskAwareSectionBase).GetField("_subscribedLoader", Priv)!.GetValue(component));
    }

    [Fact]
    public async Task Teamspeak_OnParametersSetAsync_NullLoader_DoesNotSubscribe()
    {
        var component = new Teamspeak();
        typeof(Teamspeak).GetProperty("Id")!.SetValue(component, 4);
        await RunParametersAsync(component);
        Assert.Null(typeof(ServerTaskAwareSectionBase).GetField("_subscribedLoader", Priv)!.GetValue(component));
    }

    [Fact]
    public async Task Docker_OnParametersSetAsync_NullLoader_DoesNotSubscribe()
    {
        var component = new Docker();
        typeof(Docker).GetProperty("Id")!.SetValue(component, 5);
        await RunParametersAsync(component);
        Assert.Null(typeof(ServerTaskAwareSectionBase).GetField("_subscribedLoader", Priv)!.GetValue(component));
    }

    [Fact]
    public async Task Apache_OnParametersSetAsync_NullLoader_DoesNotSubscribe()
    {
        var component = new Apache();
        typeof(Apache).GetProperty("Id")!.SetValue(component, 6);
        await RunParametersAsync(component);
        Assert.Null(typeof(ServerTaskAwareSectionBase).GetField("_subscribedLoader", Priv)!.GetValue(component));
    }

    // ── Real subscribe-on-parameters → unsubscribe-on-dispose lifecycle ────────

    [Fact]
    public async Task Rkhunter_OnParameters_Subscribes_Dispose_Unsubscribes()
    {
        var loader = CreateLoader();
        Preload(loader, 1);
        var component = new Rkhunter();
        typeof(Rkhunter).GetProperty("Id")!.SetValue(component, 1);
        typeof(Rkhunter).GetProperty("Loader")!.SetValue(component, loader);

        await RunParametersAsync(component);
        Assert.Equal(1, SubscriberCount(loader));
        Assert.Same(loader, typeof(ServerTaskAwareSectionBase).GetField("_subscribedLoader", Priv)!.GetValue(component));

        component.Dispose();
        Assert.Equal(0, SubscriberCount(loader));
    }

    [Fact]
    public async Task Mail_OnParameters_Subscribes_Dispose_Unsubscribes()
    {
        var loader = CreateLoader();
        Preload(loader, 2);
        var component = new Mail();
        typeof(Mail).GetProperty("Id")!.SetValue(component, 2);
        typeof(Mail).GetProperty("Loader")!.SetValue(component, loader);

        await RunParametersAsync(component);
        Assert.Equal(1, SubscriberCount(loader));

        component.Dispose();
        Assert.Equal(0, SubscriberCount(loader));
    }

    [Fact]
    public async Task Docker_OnParameters_TwiceWithSameLoader_SubscribesOnce()
    {
        var loader = CreateLoader();
        Preload(loader, 5);
        var component = new Docker();
        typeof(Docker).GetProperty("Id")!.SetValue(component, 5);
        typeof(Docker).GetProperty("Loader")!.SetValue(component, loader);

        await RunParametersAsync(component);
        await RunParametersAsync(component); // re-render with the same loader must not double-subscribe
        Assert.Equal(1, SubscriberCount(loader));

        component.Dispose();
        Assert.Equal(0, SubscriberCount(loader));
    }

    // ── OnTaskCompleted forwarding - null _section is a safe no-op ──────────────

    [Fact]
    public void Rkhunter_OnTaskCompleted_WithNullSection_IsNoOp()
    {
        var component = new Rkhunter();
        Assert.Null(typeof(Rkhunter).GetField("_section", Priv)!.GetValue(component));
        var method = typeof(ServerTaskAwareSectionBase).GetMethod("OnTaskCompletedAsync", Priv)!;
        var notification = new TaskCompletedNotification { ServerId = 1, TaskName = "rkhunter scan" };
        // _section is null - the handler must short-circuit without throwing.
        method.Invoke(component, [notification]);
    }

    [Fact]
    public void Mail_OnTaskCompleted_WithNullSection_IsNoOp()
    {
        var component = new Mail();
        Assert.Null(typeof(Mail).GetField("_section", Priv)!.GetValue(component));
        var method = typeof(ServerTaskAwareSectionBase).GetMethod("OnTaskCompletedAsync", Priv)!;
        var notification = new TaskCompletedNotification { ServerId = 1, TaskName = "mail check" };
        method.Invoke(component, [notification]);
    }

    [Fact]
    public void Services_OnTaskCompleted_WithNullSection_IsNoOp()
    {
        var component = new Aetheus.Front.Components.Servers.Sections.Services();
        var method = typeof(ServerTaskAwareSectionBase).GetMethod("OnTaskCompletedAsync", Priv)!;
        var notification = new TaskCompletedNotification { ServerId = 1, TaskName = "services check" };
        method.Invoke(component, [notification]);
    }

    [Fact]
    public void Docker_OnTaskCompleted_WithNullSection_IsNoOp()
    {
        var component = new Docker();
        var method = typeof(ServerTaskAwareSectionBase).GetMethod("OnTaskCompletedAsync", Priv)!;
        var notification = new TaskCompletedNotification { ServerId = 1, TaskName = "docker ps" };
        method.Invoke(component, [notification]);
    }

    [Fact]
    public void Apache_OnTaskCompleted_WithNullSection_IsNoOp()
    {
        var component = new Apache();
        var method = typeof(ServerTaskAwareSectionBase).GetMethod("OnTaskCompletedAsync", Priv)!;
        var notification = new TaskCompletedNotification { ServerId = 1, TaskName = "apache reload" };
        method.Invoke(component, [notification]);
    }
}
