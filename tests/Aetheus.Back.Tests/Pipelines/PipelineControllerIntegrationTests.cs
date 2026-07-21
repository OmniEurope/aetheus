// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Tests;

public class PipelineControllerIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = CreateAuthenticatedClient(factory);

    private const string ValidYaml = """
        name: test-pipeline
        trigger: manual
        stages:
          - name: build
            agent: test-server
            steps:
              - name: compile
                shell: dotnet build
        """;

    [Fact]
    public async Task GetPipelines_ReturnsPaginatedResult()
    {
        var result = await _client.GetFromJsonAsync<PaginatedResult<PipelineDto>>(
            "/api/pipelines?page=1&pageSize=10", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Page >= 1);
        Assert.True(result.PageSize == 10);
    }

    [Fact]
    public async Task CreatePipeline_ValidRequest_Returns201()
    {
        var request = new CreatePipelineRequest
        {
            Name = "Test Pipeline",
            Description = "A test pipeline",
            YamlDefinition = ValidYaml
        };

        var response = await _client.PostAsJsonAsync("/api/pipelines", request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var pipeline = await response.Content.ReadFromJsonAsync<PipelineDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(pipeline);
        Assert.Equal("Test Pipeline", pipeline.Name);
        Assert.True(pipeline.Id > 0);
    }

    [Fact]
    public async Task GetPipeline_AfterCreate_ReturnsPipeline()
    {
        var created = await CreateTestPipelineAsync();

        var pipeline = await _client.GetFromJsonAsync<PipelineDto>($"/api/pipelines/{created.Id}", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(pipeline);
        Assert.Equal(created.Id, pipeline.Id);
        Assert.Equal("Integration Test Pipeline", pipeline.Name);
    }

    [Fact]
    public async Task GetPipeline_NonExistent_Returns404()
    {
        var response = await _client.GetAsync("/api/pipelines/99999", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePipeline_ValidRequest_ReturnsUpdated()
    {
        var created = await CreateTestPipelineAsync();

        var updateRequest = new UpdatePipelineRequest
        {
            Name = "Updated Pipeline",
            Description = "Updated description",
            YamlDefinition = ValidYaml
        };

        var response = await _client.PutAsJsonAsync($"/api/pipelines/{created.Id}", updateRequest, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        var updated = await response.Content.ReadFromJsonAsync<PipelineDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal("Updated Pipeline", updated.Name);
    }

    [Fact]
    public async Task UpdatePipeline_NonExistent_Returns404()
    {
        var response = await _client.PutAsJsonAsync("/api/pipelines/99999", new UpdatePipelineRequest
        {
            Name = "X",
            Description = "X",
            YamlDefinition = ValidYaml
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeletePipeline_Existing_Returns204()
    {
        var created = await CreateTestPipelineAsync();

        var response = await _client.DeleteAsync($"/api/pipelines/{created.Id}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's gone
        var getResponse = await _client.GetAsync($"/api/pipelines/{created.Id}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeletePipeline_NonExistent_Returns404()
    {
        var response = await _client.DeleteAsync("/api/pipelines/99999", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ValidateYaml_ValidYaml_Returns200()
    {
        var response = await _client.PostAsJsonAsync("/api/pipelines/validate", new ValidateYamlRequest { Yaml = ValidYaml }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidateYaml_InvalidYaml_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/pipelines/validate", new ValidateYamlRequest { Yaml = "not: valid: [" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<YamlValidationResultDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("YAML", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetRuns_EmptyPipeline_ReturnsEmptyList()
    {
        var created = await CreateTestPipelineAsync();

        var runs = await _client.GetFromJsonAsync<PaginatedResult<PipelineRunDto>>(
            $"/api/pipelines/{created.Id}/runs", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(runs);
        Assert.Empty(runs.Items);
    }

    [Fact]
    public async Task TriggerRun_ExistingPipeline_ReturnsRun()
    {
        var created = await CreateTestPipelineAsync();

        var response = await _client.PostAsync($"/api/pipelines/{created.Id}/run", null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        var run = await response.Content.ReadFromJsonAsync<PipelineRunDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(run);
        Assert.Equal(created.Id, run.PipelineId);
    }

    [Fact]
    public async Task TriggerRun_NonExistent_Returns404()
    {
        var response = await _client.PostAsync("/api/pipelines/99999/run", null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRun_AfterTrigger_ReturnsRun()
    {
        var created = await CreateTestPipelineAsync();
        var triggerResponse = await _client.PostAsync($"/api/pipelines/{created.Id}/run", null, cancellationToken: TestContext.Current.CancellationToken);
        var run = await triggerResponse.Content.ReadFromJsonAsync<PipelineRunDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        var response = await _client.GetAsync($"/api/pipelines/runs/{run!.Id}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<PipelineRunDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(fetched);
        Assert.Equal(run.Id, fetched.Id);
    }

    [Fact]
    public async Task GetRun_NonExistent_Returns404()
    {
        var response = await _client.GetAsync("/api/pipelines/runs/99999", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRuns_AfterTrigger_ContainsRun()
    {
        var created = await CreateTestPipelineAsync();
        await _client.PostAsync($"/api/pipelines/{created.Id}/run", null, cancellationToken: TestContext.Current.CancellationToken);

        var runs = await _client.GetFromJsonAsync<PaginatedResult<PipelineRunDto>>(
            $"/api/pipelines/{created.Id}/runs", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(runs);
        Assert.NotEmpty(runs.Items);
    }

    private async Task<PipelineDto> CreateTestPipelineAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/pipelines", new CreatePipelineRequest
        {
            Name = "Integration Test Pipeline",
            Description = "Created by integration test",
            YamlDefinition = ValidYaml
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PipelineDto>(TestJsonOptions.Default))!;
    }

    private static HttpClient CreateAuthenticatedClient(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
        return client;
    }
}
