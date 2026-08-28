// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Front.Tests.Services;

public class UserNotificationServiceTests
{
    [Fact]
    public async Task StartAsync_DoesNothing_WhenNotAuthenticated()
    {
        var (svc, _) = CreateService(authenticated: false);

        // Subscribe BEFORE StartAsync so the assertion can actually catch a spurious event.
        var fired = false;
        svc.OnPermissionsChanged += () => fired = true;

        await svc.StartAsync();

        // No hub is created; nothing throws and no event fires.
        Assert.False(fired);
    }

    [Fact]
    public async Task DisposeAsync_WithoutStart_DoesNotThrow()
    {
        var (svc, _) = CreateService();
        await svc.DisposeAsync();
    }

    [Fact]
    public void HandleServerMessage_Debounced_RaisesOnceForBurst()
    {
        var (svc, time) = CreateService(authenticated: true, token: "jwt");
        svc.DebounceMilliseconds = 20;
        var count = 0;
        svc.OnPermissionsChanged += () => Interlocked.Increment(ref count);

        // A bulk change fires several events in quick succession (no time passes between them).
        svc.HandleServerMessage("Roles");
        svc.HandleServerMessage("Roles");
        svc.HandleServerMessage("RolePermissions");

        // Deterministic time: crossing the debounce window fires the single surviving timer exactly once.
        time.Advance(TimeSpan.FromMilliseconds(25));

        Assert.Equal(1, count);
    }

    [Fact]
    public void HandleServerMessage_SeparateBursts_RaiseSeparately()
    {
        var (svc, time) = CreateService(authenticated: true, token: "jwt");
        svc.DebounceMilliseconds = 20;
        var count = 0;
        svc.OnPermissionsChanged += () => Interlocked.Increment(ref count);

        svc.HandleServerMessage("Roles");
        time.Advance(TimeSpan.FromMilliseconds(25));
        svc.HandleServerMessage("OrganizationMembership");
        time.Advance(TimeSpan.FromMilliseconds(25));

        Assert.Equal(2, count);
    }

    [Fact]
    public void EffectivePermissionsChanged_IgnoresRoleSourceForIdenticalEffectiveGrant()
    {
        var current = new[]
        {
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Project,
                ResourceId = 7,
                Permission = Permission.Write,
                GrantedByRole = "Developers"
            }
        };
        var latest = new[]
        {
            new EffectivePermissionDto
            {
                ResourceType = ResourceType.Project,
                ResourceId = 7,
                Permission = Permission.Write,
                GrantedByRole = "Maintainers"
            }
        };

        Assert.False(UserNotificationService.EffectivePermissionsChanged(current, latest));
    }

    private static (UserNotificationService Svc, FakeTimeProvider Time) CreateService(bool authenticated = false, string token = "")
    {
        var auth = new AuthStateProvider(new FakeJsRuntime(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthStateProvider>.Instance);
        if (authenticated && !string.IsNullOrEmpty(token))
        {
            var tokenField = typeof(AuthStateProvider).GetField("_token",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            tokenField?.SetValue(auth, token);
        }

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiBaseUrl", "https://localhost:5301" } })
            .Build();
        var hubFactory = new HubConnectionFactory(config, auth,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthDelegatingHandler>.Instance);
        var api = new ApiClient(new HttpClient { BaseAddress = new Uri("https://localhost:5301/") });
        var permissions = new PermissionService();

        var time = new FakeTimeProvider();
        return (new UserNotificationService(hubFactory, auth, api, permissions, time), time);
    }
}
