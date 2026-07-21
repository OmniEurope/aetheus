// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// End-to-end exercise of the refresh-token rotation flow (F-012) through the real HTTP
/// pipeline backed by PostgreSQL.
/// <para>
/// The refresh token lifecycle is: login → receive token pair → use refresh token →
/// receive new pair → old refresh token is revoked (but honored within a 5-minute grace
/// window for in-flight concurrent requests). After the grace window, the old token is
/// fully rejected. These flows depend on real relational state (the <c>RefreshTokens</c>
/// table, hash lookups, chain revocation) that InMemory cannot model.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RefreshTokenIntegrationTests(PostgresFixture fixture)
{
    private const string AdminPassword = "Integr@tion-Test-Admin-Pwd-2026";

    [Fact]
    public async Task RefreshToken_ReturnsNewTokenPair_AndOldTokenStillWorksInGrace()
    {
        await using var factory = new AetheusWebApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        // Step 1: Login
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "admin",
            Password = AdminPassword
        }, cancellationToken: TestContext.Current.CancellationToken);
        loginResp.EnsureSuccessStatusCode();
        var firstLogin = await loginResp.Content.ReadFromJsonAsync<LoginResponse>(IntegrationJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(firstLogin);
        Assert.False(string.IsNullOrEmpty(firstLogin!.Token));
        Assert.False(string.IsNullOrEmpty(firstLogin.RefreshToken));
        Assert.False(firstLogin.TotpRequired);

        // Step 2: Use the refresh token to get a new pair
        var refreshResp = await client.PostAsJsonAsync("/api/auth/token/refresh", new RefreshTokenRequest
        {
            RefreshToken = firstLogin.RefreshToken!
        }, cancellationToken: TestContext.Current.CancellationToken);
        refreshResp.EnsureSuccessStatusCode();
        var secondLogin = await refreshResp.Content.ReadFromJsonAsync<LoginResponse>(IntegrationJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(secondLogin);
        Assert.False(string.IsNullOrEmpty(secondLogin!.Token));
        Assert.False(string.IsNullOrEmpty(secondLogin.RefreshToken));

        // The new refresh token should differ from the old one (rotation happened)
        Assert.NotEqual(firstLogin.RefreshToken, secondLogin.RefreshToken);

        // Step 3: The old refresh token should still work within the 5-minute grace window
        // (simulating a concurrent in-flight request)
        var graceResp = await client.PostAsJsonAsync("/api/auth/token/refresh", new RefreshTokenRequest
        {
            RefreshToken = firstLogin.RefreshToken!
        }, cancellationToken: TestContext.Current.CancellationToken);
        // Within the grace window, this should succeed (returns 200, not 401)
        Assert.Equal(HttpStatusCode.OK, graceResp.StatusCode);

        // Step 4: The new refresh token is still valid
        var thirdResp = await client.PostAsJsonAsync("/api/auth/token/refresh", new RefreshTokenRequest
        {
            RefreshToken = secondLogin.RefreshToken!
        }, cancellationToken: TestContext.Current.CancellationToken);
        thirdResp.EnsureSuccessStatusCode();
        var thirdLogin = await thirdResp.Content.ReadFromJsonAsync<LoginResponse>(IntegrationJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(thirdLogin!.Token));
    }

    [Fact]
    public async Task InvalidRefreshToken_Returns401()
    {
        await using var factory = new AetheusWebApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var refreshResp = await client.PostAsJsonAsync("/api/auth/token/refresh", new RefreshTokenRequest
        {
            RefreshToken = "completely-invalid-token-that-does-not-exist"
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResp.StatusCode);
    }
}
