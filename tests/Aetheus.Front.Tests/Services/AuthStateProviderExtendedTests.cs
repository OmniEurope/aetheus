// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using System.Text;
using System.Text.Json;
using Aetheus.Front.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;

namespace Aetheus.Front.Tests;

/// <summary>
/// Extended tests for AuthStateProvider covering ParseRoles, ParseClaim,
/// ShouldRenew, NotifyNeedsLogin and DecodePayload - branches not hit
/// by the existing AuthStateProviderTests.
/// </summary>
public class AuthStateProviderExtendedTests
{
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private readonly IJSRuntime _js = Substitute.For<IJSRuntime>();
    private readonly NullLogger<AuthStateProvider> _logger = NullLogger<AuthStateProvider>.Instance;

    // === ShouldRenew ===

    [Fact]
    public void ShouldRenew_NoToken_ReturnsFalse()
    {
        var sut = new AuthStateProvider(_js, _logger);
        Assert.False(sut.ShouldRenew());
    }

    [Fact]
    public async Task ShouldRenew_TokenExpiringSoon_ReturnsTrue()
    {
        var token = CreateJwt(DateTimeOffset.UtcNow.AddMinutes(3));
        var sut = new AuthStateProvider(_js, _logger);
        await sut.LoginAsync(token);

        Assert.True(sut.ShouldRenew());
    }

    [Fact]
    public async Task ShouldRenew_TokenExpiringFarFuture_ReturnsFalse()
    {
        var token = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        var sut = new AuthStateProvider(_js, _logger);
        await sut.LoginAsync(token);

        Assert.False(sut.ShouldRenew());
    }

    [Fact]
    public async Task ShouldRenew_TokenAlreadyExpired_ReturnsFalse()
    {
        var token = CreateJwt(DateTimeOffset.UtcNow.AddMinutes(-10));
        var sut = new AuthStateProvider(_js, _logger);
        await sut.LoginAsync(token);

        Assert.False(sut.ShouldRenew());
    }

    // === NotifyNeedsLogin ===

    [Fact]
    public void NotifyNeedsLogin_FiresEvent()
    {
        var sut = new AuthStateProvider(_js, _logger);
        var fired = false;
        sut.OnNeedsLogin += () => fired = true;
        sut.NotifyNeedsLogin();
        Assert.True(fired);
    }

    [Fact]
    public void NotifyNeedsLogin_NoSubscriber_DoesNotThrow()
    {
        var sut = new AuthStateProvider(_js, _logger);
        sut.NotifyNeedsLogin(); // no exception
    }

    // === ParseRoles (private) ===

    [Fact]
    public async Task ParseRoles_SingleStringRole_ReturnsOneRole()
    {
        var token = CreateJwtWithClaims(new { role = "Admin", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        var sut = new AuthStateProvider(_js, _logger);
        await sut.LoginAsync(token);

        Assert.Contains("Admin", sut.Roles);
        Assert.True(sut.IsAdmin);
    }

    [Fact]
    public async Task ParseRoles_ArrayRoles_ReturnsAll()
    {
        var token = CreateJwtWithClaims(new { role = new[] { "Admin", "User" }, exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        var sut = new AuthStateProvider(_js, _logger);
        await sut.LoginAsync(token);

        Assert.Contains("Admin", sut.Roles);
        Assert.Contains("User", sut.Roles);
    }

    [Fact]
    public async Task ParseRoles_SoapStyleKey_ReturnsRole()
    {
        var claims = new Dictionary<string, object>
        {
            ["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] = "Admin",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        };
        var token = CreateJwtFromDict(claims);
        var sut = new AuthStateProvider(_js, _logger);
        await sut.LoginAsync(token);

        Assert.True(sut.IsAdmin);
    }

    [Fact]
    public async Task ParseRoles_NoRoleClaim_ReturnsEmpty()
    {
        var token = CreateJwtWithClaims(new { sub = "user", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        var sut = new AuthStateProvider(_js, _logger);
        await sut.LoginAsync(token);

        Assert.Empty(sut.Roles);
        Assert.False(sut.IsAdmin);
    }

    // === ParseClaim / Username ===

    [Fact]
    public async Task ParseClaim_SoapStyleName_ExtractsUsername()
    {
        var claims = new Dictionary<string, object>
        {
            ["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"] = "alice",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        };
        var token = CreateJwtFromDict(claims);
        var sut = new AuthStateProvider(_js, _logger);
        await sut.LoginAsync(token);

        Assert.Equal("alice", sut.Username);
    }

    [Fact]
    public async Task ParseClaim_ShortForm_ExtractsUsername()
    {
        var claims = new Dictionary<string, object>
        {
            ["unique_name"] = "bob",
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        };
        var token = CreateJwtFromDict(claims);
        var sut = new AuthStateProvider(_js, _logger);
        await sut.LoginAsync(token);

        Assert.Equal("bob", sut.Username);
    }

    [Fact]
    public async Task ParseClaim_NoClaim_ReturnsNull()
    {
        var token = CreateJwtWithClaims(new { sub = "user", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        var sut = new AuthStateProvider(_js, _logger);
        await sut.LoginAsync(token);

        Assert.Null(sut.Username);
    }

    // === DecodePayload edge cases ===

    [Fact]
    public void DecodePayload_TooFewParts_ReturnsNull()
    {
        var method = typeof(AuthStateProvider).GetMethod("DecodePayload", PrivStatic)!;
        var result = method.Invoke(null, ["only-one-part"]);
        Assert.Null(result);
    }

    [Fact]
    public void DecodePayload_ThreeParts_WithPaddingNeeded_Succeeds()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"a\":1}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var method = typeof(AuthStateProvider).GetMethod("DecodePayload", PrivStatic)!;
        var result = (byte[]?)method.Invoke(null, [$"h.{payload}.s"]);
        Assert.NotNull(result);
    }

    // === LoginAsync with refresh token ===

    [Fact]
    public async Task LoginAsync_WithRefreshToken_StoresBoth()
    {
        var sut = new AuthStateProvider(_js, _logger);
        var token = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        await sut.LoginAsync(token, "my-refresh");

        Assert.Equal("my-refresh", sut.RefreshToken);
    }

    // === Helpers ===

    private static string CreateJwt(DateTimeOffset expiration)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}"));
        var payload = new { sub = "user", exp = expiration.ToUnixTimeSeconds() };
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var payloadBase64 = Convert.ToBase64String(payloadBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{payloadBase64}.sig";
    }

    private static string CreateJwtWithClaims(object claims)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}"));
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(claims);
        var payloadBase64 = Convert.ToBase64String(payloadBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{payloadBase64}.sig";
    }

    private static string CreateJwtFromDict(Dictionary<string, object> claims)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}"));
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(claims);
        var payloadBase64 = Convert.ToBase64String(payloadBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{payloadBase64}.sig";
    }
}
