// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Tests;

public class ServerConfigurationControllerIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = CreateAuthenticatedClient(factory);

    private const string ValidYaml = """
        server:
          name: config-test-server
          type: Normal
          tags:
            - web
            - prod
        """;

    private const string InvalidYaml = "not: valid: yaml: [";

    // ──── ExportConfiguration ────

    [Fact]
    public async Task ExportConfiguration_ExistingServer_ReturnsYaml()
    {
        var serverId = await SeedServerAsync("export-server");

        var response = await _client.GetAsync($"/api/servers/{serverId}/configuration/export", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(content));
    }

    [Fact]
    public async Task ExportConfiguration_NonExistentServer_Returns404()
    {
        var response = await _client.GetAsync("/api/servers/99999/configuration/export", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ──── ValidateConfiguration ────

    [Fact]
    public async Task ValidateConfiguration_ValidYaml_ReturnsValid()
    {
        var serverId = await SeedServerAsync("validate-server");

        var response = await _client.PostAsJsonAsync(
            $"/api/servers/{serverId}/configuration/validate",
            new ServerConfigImportRequest { Yaml = ValidYaml }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ServerConfigValidationResult>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateConfiguration_InvalidYaml_ReturnsInvalid()
    {
        var serverId = await SeedServerAsync("validate-invalid-server");

        var response = await _client.PostAsJsonAsync(
            $"/api/servers/{serverId}/configuration/validate",
            new ServerConfigImportRequest { Yaml = InvalidYaml }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ServerConfigValidationResult>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    // ──── PreviewImport ────

    [Fact]
    public async Task PreviewImport_ExistingServer_ReturnsPreview()
    {
        var serverId = await SeedServerAsync("preview-server");

        var response = await _client.PostAsJsonAsync(
            $"/api/servers/{serverId}/configuration/preview",
            new ServerConfigImportRequest { Yaml = ValidYaml }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<ServerConfigPreviewDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview);
    }

    [Fact]
    public async Task PreviewImport_NonExistentServer_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/servers/99999/configuration/preview",
            new ServerConfigImportRequest { Yaml = ValidYaml }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ──── DeployConfiguration ────

    [Fact]
    public async Task DeployConfiguration_ExistingServer_ReturnsResult()
    {
        var serverId = await SeedServerAsync("deploy-server");

        var response = await _client.PostAsJsonAsync(
            $"/api/servers/{serverId}/configuration/deploy",
            new ServerConfigImportRequest { Yaml = ValidYaml }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ServerConfigDeployResultDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DeployConfiguration_NonExistentServer_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/servers/99999/configuration/deploy",
            new ServerConfigImportRequest { Yaml = ValidYaml }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ──── Auth ────

    [Fact]
    public async Task Unauthorized_NoToken_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/servers/1/configuration/export", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ──── Helpers ────

    private async Task<int> SeedServerAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Servers.FirstOrDefaultAsync(s => s.Name == name);
        if (existing is not null) return existing.Id;

        var server = new Server
        {
            Name = name,
            Type = ServerType.Normal,
            Status = ServerStatus.Online,
            IpAddress = "10.0.0.90",
            AgentVersion = "1.0.0",
            OsDescription = "Linux",
            LastHeartbeat = DateTime.UtcNow
        };
        db.Servers.Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static HttpClient CreateAuthenticatedClient(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
        return client;
    }
}
