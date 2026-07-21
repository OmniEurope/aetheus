// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.Sections;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Tests for the thin routing-layer section pages under Pages/Servers/Sections/.
/// These pages delegate to ServerDetailLoader (cascading parameter) and forward
/// task-completed notifications to their child sections. The observable behaviour
/// asserted here is the real subscribe-on-parameters / unsubscribe-on-dispose
/// lifecycle against the loader's OnTaskCompleted event.
/// </summary>
public class SectionsTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public SectionsTests() => _handler = BunitTestHelper.RegisterServices(this);

    private ServerDetailLoader CreateLoader()
    {
        Services.AddScoped<ServerDetailLoader>();
        return new ServerDetailLoader(
            Services.GetRequiredService<ApiClient>(),
            Services.GetRequiredService<HubConnectionFactory>(),
            Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>(),
            NullLogger<ServerDetailLoader>.Instance);
    }

    /// <summary>
    /// Reads the compiler-generated backing delegate of the loader's OnTaskCompleted event
    /// and returns the number of subscribed handlers (0 when nobody is subscribed).
    /// </summary>
    private static int SubscriberCount(ServerDetailLoader loader)
    {
        var field = typeof(ServerDetailLoader).GetField("OnTaskCompleted", Priv);
        var del = (Delegate?)field!.GetValue(loader);
        return del?.GetInvocationList().Length ?? 0;
    }

    /// <summary>
    /// Pre-seeds the loader so <see cref="ServerDetailLoader.EnsureLoadedAsync"/> early-returns
    /// (its <c>_currentId == id &amp;&amp; Server is not null</c> guard) - this keeps the unit test off
    /// the SignalR hub (the test factory fails hub starts on purpose) while still driving the real
    /// subscribe block at the end of the section's <c>OnParametersSetAsync</c>.
    /// </summary>
    private static void PreloadLoader(ServerDetailLoader loader, int id)
    {
        typeof(ServerDetailLoader).GetField("_currentId", Priv)!.SetValue(loader, id);
        typeof(ServerDetailLoader).GetProperty("Server")!.SetValue(loader, new ServerDetailDto { Id = id, Name = $"srv-{id}" });
    }

    private async Task SubscribeViaParametersAsync<TComponent>(TComponent component, ServerDetailLoader loader, int id)
        where TComponent : class
    {
        PreloadLoader(loader, id);
        typeof(TComponent).GetProperty("Id")!.SetValue(component, id);
        typeof(TComponent).GetProperty("Loader")!.SetValue(component, loader);
        var onParams = typeof(TComponent).GetMethod("OnParametersSetAsync", Priv)!;
        await (Task)onParams.Invoke(component, [])!;
    }

    // The id/loader parameters carry the route arguments through to the child section.
    // We assert they round-trip (the routing page exposes them as public parameters).

    [Fact]
    public void Rkhunter_Id_Parameter_IsSet()
    {
        var component = new Rkhunter();
        typeof(Rkhunter).GetProperty("Id")!.SetValue(component, 42);
        Assert.Equal(42, component.Id);
    }

    [Fact]
    public void Rkhunter_Loader_Parameter_AcceptsNull()
    {
        var component = new Rkhunter();
        typeof(Rkhunter).GetProperty("Loader")!.SetValue(component, null);
        Assert.Null(component.Loader);
    }

    [Fact]
    public void Rkhunter_Dispose_WhenNotSubscribed_DoesNotThrow()
    {
        var component = new Rkhunter();
        // _subscribed is null → Dispose should be a no-op
        component.Dispose();
    }

    [Fact]
    public async Task Rkhunter_SubscribesOnParameters_AndUnsubscribesOnDispose()
    {
        var loader = CreateLoader();
        var component = new Rkhunter();
        Assert.Equal(0, SubscriberCount(loader));

        await SubscribeViaParametersAsync(component, loader, 1);
        Assert.Equal(1, SubscriberCount(loader));

        component.Dispose();
        Assert.Equal(0, SubscriberCount(loader));
    }

    // === Teamspeak ===

    [Fact]
    public void Teamspeak_Id_Parameter_IsSet()
    {
        var component = new Teamspeak();
        typeof(Teamspeak).GetProperty("Id")!.SetValue(component, 7);
        Assert.Equal(7, component.Id);
    }

    [Fact]
    public void Teamspeak_Dispose_WhenNotSubscribed_DoesNotThrow()
    {
        var component = new Teamspeak();
        component.Dispose();
    }

    [Fact]
    public async Task Teamspeak_SubscribesOnParameters_AndUnsubscribesOnDispose()
    {
        var loader = CreateLoader();
        var component = new Teamspeak();
        await SubscribeViaParametersAsync(component, loader, 2);
        Assert.Equal(1, SubscriberCount(loader));

        component.Dispose();
        Assert.Equal(0, SubscriberCount(loader));
    }

    // === Docker ===

    [Fact]
    public void Docker_Id_Parameter_IsSet()
    {
        var component = new Docker();
        typeof(Docker).GetProperty("Id")!.SetValue(component, 3);
        Assert.Equal(3, component.Id);
    }

    [Fact]
    public void Docker_Dispose_WhenNotSubscribed_DoesNotThrow()
    {
        var component = new Docker();
        component.Dispose();
    }

    [Fact]
    public async Task Docker_SubscribesOnParameters_AndUnsubscribesOnDispose()
    {
        var loader = CreateLoader();
        var component = new Docker();
        await SubscribeViaParametersAsync(component, loader, 3);
        Assert.Equal(1, SubscriberCount(loader));

        component.Dispose();
        Assert.Equal(0, SubscriberCount(loader));
    }

    // === Apache ===

    [Fact]
    public void Apache_Id_Parameter_IsSet()
    {
        var component = new Apache();
        typeof(Apache).GetProperty("Id")!.SetValue(component, 5);
        Assert.Equal(5, component.Id);
    }

    [Fact]
    public void Apache_Dispose_WhenNotSubscribed_DoesNotThrow()
    {
        var component = new Apache();
        component.Dispose();
    }

    [Fact]
    public async Task Apache_SubscribesOnParameters_AndUnsubscribesOnDispose()
    {
        var loader = CreateLoader();
        var component = new Apache();
        await SubscribeViaParametersAsync(component, loader, 5);
        Assert.Equal(1, SubscriberCount(loader));

        component.Dispose();
        Assert.Equal(0, SubscriberCount(loader));
    }

    // === Mail ===

    [Fact]
    public void Mail_Id_Parameter_IsSet()
    {
        var component = new Mail();
        typeof(Mail).GetProperty("Id")!.SetValue(component, 9);
        Assert.Equal(9, component.Id);
    }

    [Fact]
    public void Mail_Dispose_WhenNotSubscribed_DoesNotThrow()
    {
        var component = new Mail();
        component.Dispose();
    }

    [Fact]
    public async Task Mail_SubscribesOnParameters_AndUnsubscribesOnDispose()
    {
        var loader = CreateLoader();
        var component = new Mail();
        await SubscribeViaParametersAsync(component, loader, 9);
        Assert.Equal(1, SubscriberCount(loader));

        component.Dispose();
        Assert.Equal(0, SubscriberCount(loader));
    }

    // === Services ===

    [Fact]
    public void ServicesSection_Id_Parameter_IsSet()
    {
        var component = new Aetheus.Front.Pages.Servers.Sections.Services();
        typeof(Aetheus.Front.Pages.Servers.Sections.Services).GetProperty("Id")!.SetValue(component, 11);
        Assert.Equal(11, component.Id);
    }

    [Fact]
    public void ServicesSection_Dispose_WhenNotSubscribed_DoesNotThrow()
    {
        var component = new Aetheus.Front.Pages.Servers.Sections.Services();
        component.Dispose();
    }

    [Fact]
    public async Task ServicesSection_SubscribesOnParameters_AndUnsubscribesOnDispose()
    {
        var loader = CreateLoader();
        var component = new Aetheus.Front.Pages.Servers.Sections.Services();
        await SubscribeViaParametersAsync(component, loader, 11);
        Assert.Equal(1, SubscriberCount(loader));

        component.Dispose();
        Assert.Equal(0, SubscriberCount(loader));
    }
}
