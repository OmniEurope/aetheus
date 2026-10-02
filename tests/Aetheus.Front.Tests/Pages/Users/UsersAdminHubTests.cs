// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.SignalR.Client;
using UsersPage = Aetheus.Front.Components.Users.Users;

namespace Aetheus.Front.Tests.Pages.Users;

/// <summary>
/// Lifecycle tests for the Users page's realtime AdminHub subscription. RT4M: the hub create +
/// <c>AdminEntityChanged</c> subscribe + dispose moved into the shared <see cref="AdminEntitySubscription"/>
/// collaborator (injected as <c>AdminRt</c>); the page calls <c>StartAsync</c> in OnInitializedAsync and
/// <c>DisposeAsync</c> on teardown. These tests reach the connection through that collaborator.
///
/// Note: driving an actual <c>AdminEntityChanged</c> server-push end-to-end is not feasible in
/// bUnit - the test <c>HubConnectionFactory</c> returns a connection whose transport fails to
/// start (no SignalR server), and <see cref="HubConnection"/> exposes no public hook to dispatch
/// an incoming invocation to a registered handler. These tests therefore assert the real
/// subscribe-on-init / unsubscribe-on-dispose lifecycle, which is the observable behaviour the
/// component owns. The reload-on-User-change branch itself is covered indirectly by the grid
/// reload tests in UsersDeepTests.
/// </summary>
public class UsersAdminHubTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public UsersAdminHubTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);
    }

    private void SetupDefaults()
    {
        _handler.SetJsonResponse("api/users/roles", new List<string> { "Admin" });
        _handler.SetJsonResponse("api/users",
            new PaginatedResult<UserDto> { Items = [new UserDto { Id = 1, Username = "alice", Roles = ["Admin"] }], TotalCount = 1 });
    }

    // Reach the connection through the page-owned AdminEntitySubscription (_adminRt), which now wraps it.
    private static HubConnection? Hub(IRenderedComponent<UsersPage> cut)
    {
        var adminRt = typeof(UsersPage).GetField("_adminRt", Priv)!.GetValue(cut.Instance);
        return adminRt is null ? null : (HubConnection?)typeof(AdminEntitySubscription).GetField("_hub", Priv)!.GetValue(adminRt);
    }

    [Fact]
    public void OnInit_BuildsHubConnection_SubscriptionWired()
    {
        SetupDefaults();
        var cut = Render<UsersPage>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));

        // The page created the admin hub connection during init (handler registered on it).
        Assert.NotNull(Hub(cut));
    }

    [Fact]
    public async Task DisposeAsync_DisposesAndClearsHub()
    {
        SetupDefaults();
        var cut = Render<UsersPage>();
        cut.WaitForState(() => !cut.Markup.Contains("rz-progressbar-circular"));
        Assert.NotNull(Hub(cut));

        await cut.InvokeAsync(async () =>
            await ((IAsyncDisposable)cut.Instance).DisposeAsync());

        // Dispose unwires the subscription by disposing + nulling the connection.
        Assert.Null(Hub(cut));
    }
}
