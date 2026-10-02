// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Components.Shared;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// End-to-end exercise of the forced first-login password change cycle (S-FEAT-UE4K / S-TECH-MDP7)
/// through the real HTTP pipeline backed by PostgreSQL.
/// <para>
/// The documented contract is NOT a 403 block: a user created with <c>MustChangePassword = true</c>
/// logs in successfully, but the response carries <c>MustChangePassword = true</c> AND the minted JWT
/// carries the <c>aetheus:mcp</c> claim (<see cref="AetheusClaimTypes.MustChangePassword"/>) so
/// the Front can gate navigation onto the change-password screen. After a self-service change, the
/// flag is cleared in the DB and a fresh login no longer sets <c>MustChangePassword</c> nor mints the
/// <c>aetheus:mcp</c> claim. This whole flag → change → cleared cycle depends on real persisted
/// state and a real token mint, which the InMemory suite cannot model.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ForcedPasswordChangeIntegrationTests(PostgresFixture fixture)
{
    private const string AdminPassword = AetheusWebApplicationFactory.AdminPassword;

    [Fact]
    public async Task FirstLogin_WithForcedChange_FlagsToken_ThenClearsAfterChange()
    {
        await using var factory = new AetheusWebApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        // 1. As admin, create a user that must change its password on first login.
        var adminToken = await IntegrationAuth.LoginAsAdminAsync(client, TestContext.Current.CancellationToken);
        IntegrationAuth.SetBearer(client, adminToken);

        var username = $"mcp-user-{Guid.NewGuid():N}";
        const string initialPassword = "Forced-Change-Pwd-2026!";
        const string newPassword = "Chosen-New-Pwd-2026!";

        var createResp = await client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Username = username,
            Password = initialPassword,
            Roles = ["Reader"],
            MustChangePassword = true
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        // 2. First login succeeds but is flagged: the response says MustChangePassword and the JWT
        //    carries the aetheus:mcp claim so the Front can force the change-password screen.
        IntegrationAuth.SetBearer(client, null);
        var firstLogin = await LoginAsync(client, username, initialPassword);
        Assert.True(firstLogin.MustChangePassword);
        Assert.False(string.IsNullOrEmpty(firstLogin.Token));
        Assert.True(TokenHasForcedChangeClaim(firstLogin.Token),
            "First-login token must carry the aetheus:mcp claim while MustChangePassword is set.");

        // 3. Change the password as that user (self-service requires the current password).
        IntegrationAuth.SetBearer(client, firstLogin.Token);
        var changeResp = await client.PostAsJsonAsync("/api/users/me/change-password", new ChangeUserPasswordRequest
        {
            CurrentPassword = initialPassword,
            NewPassword = newPassword
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, changeResp.StatusCode);

        // 4. Re-login with the new password: the forced-change signal is gone, on both the response
        //    flag and the freshly minted token (no aetheus:mcp claim).
        IntegrationAuth.SetBearer(client, null);
        var secondLogin = await LoginAsync(client, username, newPassword);
        Assert.False(secondLogin.MustChangePassword);
        Assert.False(string.IsNullOrEmpty(secondLogin.Token));
        Assert.False(TokenHasForcedChangeClaim(secondLogin.Token),
            "A token minted after the change must NOT carry the aetheus:mcp claim.");

        // 5. The old (now-stale) initial password no longer authenticates.
        var staleResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = initialPassword
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, staleResp.StatusCode);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = password
        });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(IntegrationJsonOptions.Default);
        return login!;
    }

    /// <summary>
    /// Decodes the JWT payload (base64url, no signature validation needed - we only inspect the
    /// claim set the server minted) and reports whether the forced-change claim is present.
    /// </summary>
    private static bool TokenHasForcedChangeClaim(string jwt)
    {
        var segments = jwt.Split('.');
        Assert.True(segments.Length == 3, "Malformed JWT - expected three dot-separated segments.");

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(segments[1]));
        using var doc = JsonDocument.Parse(payloadJson);
        return doc.RootElement.TryGetProperty(AetheusClaimTypes.MustChangePassword, out _);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };
        return Convert.FromBase64String(padded);
    }
}
