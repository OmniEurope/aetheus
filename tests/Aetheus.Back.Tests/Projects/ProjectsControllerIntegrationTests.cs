// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests;

public class ProjectsControllerIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = CreateAuthenticatedClient(factory);

    [Fact]
    public async Task GetProjects_ReturnsPaginatedResult()
    {
        var result = await _client.GetFromJsonAsync<PaginatedResult<ProjectDto>>(
            "/api/projects?page=1&pageSize=10", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Page >= 1);
        Assert.Equal(10, result.PageSize);
    }

    [Fact]
    public async Task CreateProject_ValidRequest_Returns201()
    {
        var request = new CreateProjectRequest
        {
            Name = "Test Project",
            Description = "Integration test project",
            Tags = ["test", "ci"]
        };

        var response = await _client.PostAsJsonAsync("/api/projects", request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<ProjectDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(project);
        Assert.Equal("Test Project", project.Name);
        Assert.True(project.Id > 0);
    }

    [Fact]
    public async Task GetProject_AfterCreate_ReturnsDetail()
    {
        var created = await CreateTestProjectAsync();

        var detail = await _client.GetFromJsonAsync<ProjectDetailDto>($"/api/projects/{created.Id}", TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(detail);
        Assert.Equal(created.Id, detail.Id);
        Assert.Equal("IntegrationProject", detail.Name);
    }

    [Fact]
    public async Task GetProject_NonExistent_Returns404()
    {
        var response = await _client.GetAsync("/api/projects/99999", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProject_ValidRequest_ReturnsUpdated()
    {
        var created = await CreateTestProjectAsync();

        var response = await _client.PutAsJsonAsync($"/api/projects/{created.Id}", new UpdateProjectRequest
        {
            Name = "Updated Project",
            Description = "Updated",
            Status = ProjectStatus.Archived,
            Tags = ["updated"]
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProjectDto>(TestJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(updated);
        Assert.Equal("Updated Project", updated.Name);
    }

    [Fact]
    public async Task UpdateProject_NonExistent_Returns404()
    {
        var response = await _client.PutAsJsonAsync("/api/projects/99999", new UpdateProjectRequest
        {
            Name = "X",
            Description = "X"
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteProject_Existing_Returns204()
    {
        var created = await CreateTestProjectAsync();

        var response = await _client.DeleteAsync($"/api/projects/{created.Id}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await _client.GetAsync($"/api/projects/{created.Id}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteProject_NonExistent_Returns404()
    {
        var response = await _client.DeleteAsync("/api/projects/99999", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_NoToken_Returns401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/projects", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<ProjectDto> CreateTestProjectAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new CreateProjectRequest
        {
            Name = "IntegrationProject",
            Description = "Created by integration test"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(TestJsonOptions.Default))!;
    }

    [Fact]
    public async Task GetProjectServers_ReturnsServerList()
    {
        var project = await CreateTestProjectAsync();

        var response = await _client.GetAsync($"/api/projects/{project.Id}/servers", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProjectTasks_ReturnsPaginatedResult()
    {
        var project = await CreateTestProjectAsync();

        var response = await _client.GetAsync($"/api/projects/{project.Id}/tasks?page=1&pageSize=10", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProjectLogs_ReturnsPaginatedResult()
    {
        var project = await CreateTestProjectAsync();

        var response = await _client.GetAsync($"/api/projects/{project.Id}/logs?page=1&pageSize=10", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetProjectActivity_ReturnsList()
    {
        var project = await CreateTestProjectAsync();

        var response = await _client.GetAsync($"/api/projects/{project.Id}/activity?count=5", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpClient CreateAuthenticatedClient(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CustomWebApplicationFactory.GenerateTestToken());
        return client;
    }
}
