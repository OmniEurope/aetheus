// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// Shared HTTP-login helpers for the integration suites. Before this existed, four suites
/// (DevController reset guard, ResourceAuthorization, SignalRBroadcast, ArtifactDownload) each
/// carried an identical <c>LoginAsAdminAsync</c> copy - drift bait if the login contract ever
/// changes. Centralised here so there is a single place to keep the login round-trip correct
/// (S-TECH-LGN4).
/// </summary>
internal static class IntegrationAuth
{
    /// <summary>The bootstrap-admin password the integration <see cref="AetheusWebApplicationFactory"/> seeds.</summary>
    public const string AdminPassword = AetheusWebApplicationFactory.AdminPassword;

    /// <summary>Logs in over HTTP and returns the JWT, throwing if the login does not succeed.</summary>
    public static async Task<string> LoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = username,
            Password = password
        }, ct);
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(IntegrationJsonOptions.Default, ct);
        return login!.Token;
    }

    /// <summary>Logs in as the seeded bootstrap admin and returns the JWT.</summary>
    public static Task<string> LoginAsAdminAsync(HttpClient client, CancellationToken ct)
        => LoginAsync(client, "admin", AdminPassword, ct);

    /// <summary>Sets (or clears, when <paramref name="token"/> is null) the Bearer header on a client.</summary>
    public static void SetBearer(HttpClient client, string? token)
        => client.DefaultRequestHeaders.Authorization =
            token is null ? null : new AuthenticationHeaderValue("Bearer", token);
}
