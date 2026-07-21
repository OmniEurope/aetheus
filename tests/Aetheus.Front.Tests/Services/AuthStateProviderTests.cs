// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.Json;
using Aetheus.Front.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Aetheus.Front.Tests;

public class AuthStateProviderTests
{
    private readonly IJSRuntime _jsMock = Substitute.For<IJSRuntime>();
    private readonly NullLogger<AuthStateProvider> _logger = NullLogger<AuthStateProvider>.Instance;

    [Fact]
    public void IsAuthenticated_NoToken_ReturnsFalse()
    {
        var sut = new AuthStateProvider(_jsMock, _logger);

        Assert.False(sut.IsAuthenticated);
        Assert.Null(sut.Token);
    }

    [Fact]
    public async Task LoginAsync_SetsTokenAndNotifies()
    {
        var sut = new AuthStateProvider(_jsMock, _logger);
        var notified = false;
        sut.OnAuthStateChanged += () => notified = true;

        await sut.LoginAsync("test-token");

        Assert.Equal("test-token", sut.Token);
        Assert.True(sut.IsAuthenticated);
        Assert.True(notified);
    }

    [Fact]
    public async Task LogoutAsync_ClearsTokenAndNotifies()
    {
        var sut = new AuthStateProvider(_jsMock, _logger);
        await sut.LoginAsync("token");

        var notified = false;
        sut.OnAuthStateChanged += () => notified = true;

        await sut.LogoutAsync();

        Assert.Null(sut.Token);
        Assert.False(sut.IsAuthenticated);
        Assert.True(notified);
    }

    [Fact]
    public async Task InitializeAsync_ValidToken_KeepsToken()
    {
        var token = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        // First getItem = access token, second getItem = refresh token (null)
        _jsMock.InvokeAsync<string?>("localStorage.getItem",
            Arg.Any<object[]>())
            .Returns(token, (string?)null);

        var sut = new AuthStateProvider(_jsMock, _logger);
        await sut.InitializeAsync();

        Assert.Equal(token, sut.Token);
        Assert.True(sut.IsAuthenticated);
    }

    [Fact]
    public async Task InitializeAsync_ExpiredToken_NoRefresh_Clears()
    {
        var token = CreateJwt(DateTimeOffset.UtcNow.AddHours(-1));
        // First call returns access token, second returns null (no refresh token)
        _jsMock.InvokeAsync<string?>("localStorage.getItem",
            Arg.Any<object[]>())
            .Returns(token, (string?)null);

        var sut = new AuthStateProvider(_jsMock, _logger);
        await sut.InitializeAsync();

        Assert.Null(sut.Token);
        Assert.False(sut.IsAuthenticated);
    }

    [Fact]
    public async Task InitializeAsync_ExpiredToken_WithRefresh_KeepsToken()
    {
        var token = CreateJwt(DateTimeOffset.UtcNow.AddHours(-1));
        // First call returns access token, second returns refresh token
        _jsMock.InvokeAsync<string?>("localStorage.getItem",
            Arg.Any<object[]>())
            .Returns(token, "refresh-token-value");

        var sut = new AuthStateProvider(_jsMock, _logger);
        await sut.InitializeAsync();

        // Token kept - AuthDelegatingHandler will use the refresh token on next request
        Assert.Equal(token, sut.Token);
        Assert.Equal("refresh-token-value", sut.RefreshToken);
    }

    [Fact]
    public async Task InitializeAsync_MalformedToken_Clears()
    {
        _jsMock.InvokeAsync<string?>("localStorage.getItem",
            Arg.Any<object[]>())
            .Returns("not.a.jwt", (string?)null);

        var sut = new AuthStateProvider(_jsMock, _logger);
        await sut.InitializeAsync();

        Assert.Null(sut.Token);
    }

    [Fact]
    public async Task InitializeAsync_NullToken_StaysNull()
    {
        _jsMock.InvokeAsync<string?>("localStorage.getItem",
            Arg.Any<object[]>())
            .Returns((string?)null, (string?)null);

        var sut = new AuthStateProvider(_jsMock, _logger);
        await sut.InitializeAsync();

        Assert.Null(sut.Token);
        Assert.False(sut.IsAuthenticated);
    }

    [Fact]
    public async Task InitializeAsync_JsException_TokenStaysNull()
    {
        _jsMock.InvokeAsync<string?>("localStorage.getItem",
            Arg.Any<object[]>()).ThrowsAsync(new JSException("JS error"));

        var sut = new AuthStateProvider(_jsMock, _logger);
        await sut.InitializeAsync();

        Assert.Null(sut.Token);
    }

    [Fact]
    public async Task InitializeAsync_TokenWithoutExpClaim_Clears()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":\"user\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"header.{payload}.signature";
        _jsMock.InvokeAsync<string?>("localStorage.getItem",
            Arg.Any<object[]>())
            .Returns(token, (string?)null);

        var sut = new AuthStateProvider(_jsMock, _logger);
        await sut.InitializeAsync();

        Assert.Null(sut.Token);
    }

    private static string CreateJwt(DateTimeOffset expiration)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = new { sub = "user", exp = expiration.ToUnixTimeSeconds() };
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var payloadBase64 = Convert.ToBase64String(payloadBytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{payloadBase64}.signature";
    }
}
