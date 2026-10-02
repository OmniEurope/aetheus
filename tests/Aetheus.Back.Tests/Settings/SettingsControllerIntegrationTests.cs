// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Tests;

public class SettingsControllerIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = CreateAuthenticatedClient(factory);

    [Fact]
    public async Task GetSettings_Returns200()
    {
        var response = await _client.GetAsync("/api/settings", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await response.Content.ReadFromJsonAsync<List<AppSettingDto>>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(settings);
    }

    [Fact]
    public async Task GetSecrets_Returns200()
    {
        var response = await _client.GetAsync("/api/settings/secrets", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var secrets = await response.Content.ReadFromJsonAsync<List<SecretDto>>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(secrets);
    }

    [Fact]
    public async Task CreateSecret_ValidRequest_Returns200()
    {
        var request = new CreateSecretRequest
        {
            Key = $"test-key-{Guid.NewGuid():N}",
            Value = "test-value"
        };

        var response = await _client.PostAsJsonAsync("/api/settings/secrets", request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var secret = await response.Content.ReadFromJsonAsync<SecretDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(secret);
        Assert.Equal(request.Key, secret.Key);
    }

    [Fact]
    public async Task DeleteSecret_AfterCreate_Returns204()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/settings/secrets", new CreateSecretRequest
        {
            Key = $"delete-me-{Guid.NewGuid():N}",
            Value = "temp"
        }, cancellationToken: TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<SecretDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        var response = await _client.DeleteAsync($"/api/settings/secrets/{created!.Id}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSecret_NonExistent_Returns404()
    {
        var response = await _client.DeleteAsync("/api/settings/secrets/99999", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSetting_ExistingKey_Returns200()
    {
        // Seed a setting first
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                db.AppSettings, s => s.Key == "Theme", cancellationToken: TestContext.Current.CancellationToken))
            {
                db.AppSettings.Add(new AppSetting { Key = "Theme", Value = "dark" });
                await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
            }
        }

        var response = await _client.PutAsJsonAsync("/api/settings/Theme",
            new AppSettingDto { Key = "Theme", Value = "light" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_NoToken_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/settings", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpClient CreateAuthenticatedClient(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
        return client;
    }
}
