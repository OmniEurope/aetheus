// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Aetheus.Back.Tests;

public class AgentInstallerControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly HttpClient _anonClient;

    public AgentInstallerControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
        _anonClient = _factory.CreateClient();
    }

    private async Task<string> CreateRegistrationTokenAsync(int hours = 24)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/registration-tokens",
            new CreateRegistrationTokenRequest { ExpirationHours = hours });
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<RegistrationTokenDto>(TestJsonOptions.Default);
        return dto!.Token;
    }

    private async Task<HttpResponseMessage> GetInstallerAsync(string platform, string regToken, string? version = null)
    {
        var url = $"/api/agent/installer/{platform}";
        if (version is not null) url += $"?version={version}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
        request.Headers.Add("X-Registration-Token", regToken);
        return await _factory.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task GetInstaller_Linux_ValidToken_ReturnsBashScript()
    {
        var token = await CreateRegistrationTokenAsync();
        var response = await GetInstallerAsync("linux", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/x-shellscript", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("#!/usr/bin/env bash", body);
        Assert.Contains(token, body);
        Assert.Contains("AGENT_VERSION=", body);
    }

    [Fact]
    public async Task GetInstaller_Windows_ValidToken_ReturnsPowerShellScript()
    {
        var token = await CreateRegistrationTokenAsync();
        var response = await GetInstallerAsync("windows", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("$RegistrationToken", body);
        Assert.Contains(token, body);
        Assert.Contains("$AgentVersion", body);
    }

    [Fact]
    public async Task GetInstaller_NoVersion_DefaultsToConfiguredVersion()
    {
        var token = await CreateRegistrationTokenAsync();
        var response = await GetInstallerAsync("linux", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken);
        // Falls back to "App:Version" or "dev"; either way an AGENT_VERSION line exists.
        Assert.Matches(@"AGENT_VERSION=""[^""]+""", body);
    }

    [Fact]
    public async Task GetInstaller_ExplicitVersion_PropagatesIntoScript()
    {
        var token = await CreateRegistrationTokenAsync();
        var response = await GetInstallerAsync("linux", token, "1.2.3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("AGENT_VERSION=\"1.2.3\"", body);
    }

    [Fact]
    public async Task GetInstaller_UnknownPlatform_Returns400()
    {
        var token = await CreateRegistrationTokenAsync();
        var response = await GetInstallerAsync("macos", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetInstaller_MissingToken_Returns400()
    {
        var response = await _client.GetAsync("/api/agent/installer/linux", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetInstaller_BlankToken_Returns400()
    {
        var response = await _client.GetAsync("/api/agent/installer/linux?token=", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetInstaller_LegacyQueryToken_RemainsAvailableOutsideDevelopment()
    {
        var token = await CreateRegistrationTokenAsync();

        var response = await _client.GetAsync(
            $"/api/agent/installer/linux?token={Uri.EscapeDataString(token)}",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(token, body);
    }

    [Fact]
    public async Task GetInstaller_InvalidToken_Returns400()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/agent/installer/linux");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
        request.Headers.Add("X-Registration-Token", "not-a-real-token");
        var response = await _factory.CreateClient().SendAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetInstaller_Unauthenticated_Returns401()
    {
        var response = await _anonClient.GetAsync("/api/agent/installer/linux?token=anything", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
