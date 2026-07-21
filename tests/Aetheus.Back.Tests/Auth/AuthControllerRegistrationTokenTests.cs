// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Tests;

public class AuthControllerRegistrationTokenTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
        return client;
    }

    [Fact]
    public async Task CreateRegistrationToken_DefaultExpiration_Returns200()
    {
        var client = CreateAuthenticatedClient();
        var request = new CreateRegistrationTokenRequest();

        var response = await client.PostAsJsonAsync("/api/auth/registration-tokens", request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<RegistrationTokenDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(token);
        Assert.False(string.IsNullOrEmpty(token.Token));
        Assert.False(token.IsUsed);
        Assert.Null(token.UsedByServerId);
        Assert.True(token.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task CreateRegistrationToken_CustomExpiration_SetsCorrectExpiry()
    {
        var client = CreateAuthenticatedClient();
        var request = new CreateRegistrationTokenRequest { ExpirationHours = 48 };

        var response = await client.PostAsJsonAsync("/api/auth/registration-tokens", request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<RegistrationTokenDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(token);
        var expectedExpiry = DateTime.UtcNow.AddHours(48);
        Assert.InRange(token.ExpiresAt, expectedExpiry.AddMinutes(-1), expectedExpiry.AddMinutes(1));
    }

    [Fact]
    public async Task GetRegistrationTokens_Returns200_WithList()
    {
        var client = CreateAuthenticatedClient();

        // Create a token first
        await client.PostAsJsonAsync("/api/auth/registration-tokens", new CreateRegistrationTokenRequest(), cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/api/auth/registration-tokens", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<List<RegistrationTokenDto>>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(tokens);
        Assert.NotEmpty(tokens);
    }

    [Fact]
    public async Task GetRegistrationToken_WithValidId_Returns200()
    {
        var client = CreateAuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync("/api/auth/registration-tokens", new CreateRegistrationTokenRequest(), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<RegistrationTokenDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/auth/registration-tokens/{created.Id}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<RegistrationTokenDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(token);
        Assert.Equal(created.Id, token.Id);
    }

    [Fact]
    public async Task GetRegistrationToken_WithUnknownId_Returns404()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/auth/registration-tokens/999999", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRegistrationToken_AsNonAdmin_Returns403()
    {
        // Seed a token with the admin client, then read it back as a non-admin user.
        var adminClient = CreateAuthenticatedClient();
        var createResponse = await adminClient.PostAsJsonAsync("/api/auth/registration-tokens", new CreateRegistrationTokenRequest(), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<RegistrationTokenDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        var userClient = factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.UserToken());

        var response = await userClient.GetAsync($"/api/auth/registration-tokens/{created.Id}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RegisterServer_WithValidToken_Returns200()
    {
        var client = CreateAuthenticatedClient();

        // Create a registration token
        var createResponse = await client.PostAsJsonAsync("/api/auth/registration-tokens", new CreateRegistrationTokenRequest(), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<RegistrationTokenDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        // Register a server using that token
        var regRequest = new ServerRegistrationRequest
        {
            RegistrationToken = created.Token,
            Hostname = "test-server-01",
            OsDescription = "Linux 6.1",
            AgentVersion = "1.0.0",
            IpAddress = "10.0.0.1"
        };

        var response = await client.PostAsJsonAsync("/api/auth/register", regRequest, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ServerRegistrationResponse>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.True(result.ServerId > 0);
        Assert.False(string.IsNullOrEmpty(result.BearerToken));
    }

    [Fact]
    public async Task RegisterServer_WithUsedToken_Returns400()
    {
        var client = CreateAuthenticatedClient();

        // Create a registration token
        var createResponse = await client.PostAsJsonAsync("/api/auth/registration-tokens", new CreateRegistrationTokenRequest(), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<RegistrationTokenDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        // First registration succeeds
        var regRequest = new ServerRegistrationRequest
        {
            RegistrationToken = created.Token,
            Hostname = "server-first-use",
            OsDescription = "Linux",
            AgentVersion = "1.0.0"
        };
        var first = await client.PostAsJsonAsync("/api/auth/register", regRequest, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second registration with same token fails
        var second = await client.PostAsJsonAsync("/api/auth/register", new ServerRegistrationRequest
        {
            RegistrationToken = created.Token,
            Hostname = "server-second-use",
            OsDescription = "Linux",
            AgentVersion = "1.0.0"
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task RegisterServer_WithInvalidToken_Returns400()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new ServerRegistrationRequest
        {
            RegistrationToken = "non-existent-token",
            Hostname = "rogue-server",
            OsDescription = "Linux",
            AgentVersion = "1.0.0"
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRegistrationTokens_AfterRegistration_ShowsTokenAsUsed()
    {
        var client = CreateAuthenticatedClient();

        // Create and use a token
        var createResponse = await client.PostAsJsonAsync("/api/auth/registration-tokens", new CreateRegistrationTokenRequest(), cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<RegistrationTokenDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(created);

        await client.PostAsJsonAsync("/api/auth/register", new ServerRegistrationRequest
        {
            RegistrationToken = created.Token,
            Hostname = "used-server",
            OsDescription = "Linux",
            AgentVersion = "1.0.0"
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Verify token is now marked as used
        var listResponse = await client.GetAsync("/api/auth/registration-tokens", cancellationToken: TestContext.Current.CancellationToken);
        var tokens = await listResponse.Content.ReadFromJsonAsync<List<RegistrationTokenDto>>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(tokens);

        var usedToken = tokens.FirstOrDefault(t => t.Id == created.Id);
        Assert.NotNull(usedToken);
        Assert.True(usedToken.IsUsed);
        Assert.NotNull(usedToken.UsedByServerId);
    }
}
