// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Services;

/// <summary>
/// Covers finding #5: a failed initial connect used to leave <see cref="UserNotificationService"/>
/// permanently blind for the session, because reconciliation only ran from <c>Reconnected</c>, which
/// never fires unless the hub connected at least once. Uses the bUnit harness (whose
/// <c>HubConnectionFactory</c> always fails to connect immediately) so the failure path is exercised
/// deterministically, mirroring <c>TaskTrackerServiceReconnectTests</c>.
/// </summary>
public class UserNotificationServiceReconnectTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    public UserNotificationServiceReconnectTests() => BunitTestHelper.RegisterServices(this);

    private UserNotificationService CreateService() => new(
        Services.GetRequiredService<HubConnectionFactory>(),
        Services.GetRequiredService<AuthStateProvider>(),
        Services.GetRequiredService<ApiClient>(),
        Services.GetRequiredService<PermissionService>(),
        TimeProvider.System);

    private static object? GetPrivate(UserNotificationService sut, string field) =>
        typeof(UserNotificationService).GetField(field, Priv)!.GetValue(sut);

    [Fact]
    public async Task StartAsync_InitialConnectFails_DoesNotThrow_AndArmsRetryLoop()
    {
        var sut = CreateService();

        await sut.StartAsync();

        // The hub is created and assigned (StartAsync never nulls it back out on failure), and a
        // retry loop is armed so the service is not permanently blind for the rest of the session.
        Assert.NotNull(GetPrivate(sut, "_hub"));
        Assert.NotNull(GetPrivate(sut, "_startRetryCts"));

        await sut.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_AfterFailedStart_CancelsRetryLoop_DoesNotThrow()
    {
        var sut = CreateService();
        await sut.StartAsync();

        await sut.DisposeAsync();

        // Disposal must cleanly cancel the pending retry loop (no lingering CTS reference) rather
        // than leaving a zombie background task running against a disposed service.
        Assert.Null(GetPrivate(sut, "_startRetryCts"));
        Assert.Null(GetPrivate(sut, "_hub"));
    }

    [Fact]
    public async Task StartAsync_SecondCallWhileRetrying_StaysIdempotent()
    {
        var sut = CreateService();
        await sut.StartAsync();
        var hubBefore = GetPrivate(sut, "_hub");

        await sut.StartAsync();

        Assert.Same(hubBefore, GetPrivate(sut, "_hub"));

        await sut.DisposeAsync();
    }
}
