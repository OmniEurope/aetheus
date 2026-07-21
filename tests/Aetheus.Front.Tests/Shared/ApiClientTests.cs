// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Front.Tests;

public class ApiClientTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static (ApiClient client, TestHandler handler) Create()
    {
        var handler = new TestHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        return (new ApiClient(http), handler);
    }

    private static StringContent Json<T>(T obj) =>
        new(JsonSerializer.Serialize(obj, JsonOpts), System.Text.Encoding.UTF8, "application/json");

    // --- Auth ---

    [Fact]
    public async Task LoginAsync_Success_ReturnsToken()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new LoginResponse { Token = "tok" }) };
        var result = await api.LoginAsync(new LoginRequest { Username = "u", Password = "p" }, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("tok", result!.Token);
    }

    [Fact]
    public async Task LoginAsync_Failure_ReturnsNull()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var result = await api.LoginAsync(new LoginRequest { Username = "u", Password = "p" }, Xunit.TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRegistrationTokensAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new List<RegistrationTokenDto> { new() { Id = 1, Token = "t" } })
        };
        var result = await api.GetRegistrationTokensAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task CreateRegistrationTokenAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new RegistrationTokenDto { Id = 1, Token = "new" })
        };
        var result = await api.CreateRegistrationTokenAsync(48, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateRegistrationTokenAsync_Failure_ReturnsNull()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        var result = await api.CreateRegistrationTokenAsync(ct: Xunit.TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    // --- Servers ---

    [Fact]
    public async Task GetServersAsync_ReturnsPaginatedResult()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<ServerDto> { Items = [new() { Id = 1, Name = "s1" }], TotalCount = 1 })
        };
        var result = await api.GetServersAsync(1, 25, "test", ServerType.Docker, ServerStatus.Online, "Name", true);
        Assert.Single(result.Items);
        Assert.Contains("search=test", h.LastRequest!.RequestUri!.ToString());
        Assert.Contains("type=Docker", h.LastRequest.RequestUri.ToString());
        Assert.Contains("status=Online", h.LastRequest.RequestUri.ToString());
        Assert.Contains("sortBy=Name", h.LastRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task GetServerDetailAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new ServerDetailDto { Id = 5, Name = "srv" })
        };
        var result = await api.GetServerDetailAsync(5, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(5, result!.Id);
    }

    [Fact]
    public async Task UpdateServerAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ServerDto { Id = 1 }) };
        var result = await api.UpdateServerAsync(1, new UpdateServerRequest(), Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpdateServerAsync_Failure_ReturnsNull()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        var result = await api.UpdateServerAsync(1, new UpdateServerRequest(), Xunit.TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteServerAsync_ReturnsTrue()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteServerAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteServerAsync_ReturnsFalse()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.False(await api.DeleteServerAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    // --- Tasks ---

    [Fact]
    public async Task GetTasksAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<ServerTaskDto> { Items = [], TotalCount = 0 })
        };
        var result = await api.GetTasksAsync(2, 10);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task CreateTaskAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ServerTaskDto { Id = 1 }) };
        var result = await api.CreateTaskAsync(new CreateTaskRequest());
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateTaskAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateTaskAsync(new CreateTaskRequest()));
    }

    // --- Pipelines ---

    [Fact]
    public async Task GetPipelinesAsync_WithFilters()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<PipelineDto> { Items = [], TotalCount = 0 })
        };
        await api.GetPipelinesAsync(search: "build", triggerType: PipelineTriggerType.Webhook);
        Assert.Contains("search=build", h.LastRequest!.RequestUri!.ToString());
        Assert.Contains("triggerType=Webhook", h.LastRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task GetPipelineAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PipelineDto { Id = 3 }) };
        var result = await api.GetPipelineAsync(3, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(3, result!.Id);
    }

    [Fact]
    public async Task CreatePipelineAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PipelineDto { Id = 1 }) };
        var result = await api.CreatePipelineAsync(new CreatePipelineRequest(), Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result.Value);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task CreatePipelineAsync_InvalidYaml_ReturnsValidationErrors()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = Json(new YamlValidationResultDto { IsValid = false, Errors = ["bad stage"] })
        };
        var result = await api.CreatePipelineAsync(new CreatePipelineRequest(), Xunit.TestContext.Current.CancellationToken);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.Contains("bad stage", result.Error.Errors);
    }

    [Fact]
    public async Task UpdatePipelineAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PipelineDto { Id = 2 }) };
        var result = await api.UpdatePipelineAsync(2, new UpdatePipelineRequest(), Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Id);
    }

    [Fact]
    public async Task UpdatePipelineAsync_InvalidYaml_ReturnsValidationErrors()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = Json(new YamlValidationResultDto { IsValid = false, Errors = ["bad step"] })
        };
        var result = await api.UpdatePipelineAsync(2, new UpdatePipelineRequest(), Xunit.TestContext.Current.CancellationToken);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
        Assert.Contains("bad step", result.Error.Errors);
    }

    [Fact]
    public async Task DeletePipelineAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeletePipelineAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StatusEndpoint_404_ReportsNotFound_AndConvertsToFalse()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var status = await api.DeletePipelineAsync(1, Xunit.TestContext.Current.CancellationToken);

        Assert.False(status.Success);
        Assert.True(status.NotFound);
        Assert.False(status.Forbidden);
        Assert.False(status); // implicit bool - keeps `if (await Api.X())` call sites working
    }

    [Fact]
    public async Task StatusEndpoint_403_ReportsForbidden()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.Forbidden);

        var status = await api.DeletePipelineAsync(1, Xunit.TestContext.Current.CancellationToken);

        Assert.False(status.Success);
        Assert.True(status.Forbidden);
        Assert.False(status.NotFound);
    }

    [Fact]
    public async Task TriggerPipelineRunAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PipelineRunDto { Id = 10 }) };
        var result = await api.TriggerPipelineRunAsync(5, ct: Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result.Value);
        Assert.Equal(10, result.Value.Id);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.False(result.NotFound);
    }

    [Fact]
    public async Task TriggerPipelineRunAsync_InvalidYaml_ReturnsValidationErrors()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = Json(new YamlValidationResultDto { IsValid = false, Errors = ["YAML syntax error"] })
        };
        var result = await api.TriggerPipelineRunAsync(5, ct: Xunit.TestContext.Current.CancellationToken);
        Assert.Null(result.Value);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("YAML syntax error", result.Error.Errors);
        Assert.False(result.NotFound);
    }

    [Fact]
    public async Task TriggerPipelineRunAsync_NotFound_FlagsNotFound()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var result = await api.TriggerPipelineRunAsync(5, ct: Xunit.TestContext.Current.CancellationToken);
        Assert.Null(result.Value);
        Assert.Null(result.Error);
        Assert.True(result.NotFound);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task GetPipelineRunsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PaginatedResult<PipelineRunDto>()) };
        var result = await api.GetPipelineRunsAsync(1);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPipelineRunAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PipelineRunDto { Id = 7 }) };
        var result = await api.GetPipelineRunAsync(7, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(7, result!.Id);
    }

    // --- Projects ---

    [Fact]
    public async Task GetProjectsAsync_WithSearch()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<ProjectDto> { Items = [], TotalCount = 0 })
        };
        await api.GetProjectsAsync(search: "web");
        Assert.Contains("search=web", h.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetProjectDetailAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ProjectDetailDto { Id = 2 }) };
        var result = await api.GetProjectDetailAsync(2, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(2, result!.Id);
    }

    [Fact]
    public async Task CreateProjectAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ProjectDto { Id = 1 }) };
        Assert.NotNull(await api.CreateProjectAsync(new CreateProjectRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateProjectAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateProjectAsync(new CreateProjectRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateProjectAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ProjectDto { Id = 1 }) };
        Assert.NotNull(await api.UpdateProjectAsync(1, new UpdateProjectRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateProjectAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.UpdateProjectAsync(1, new UpdateProjectRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteProjectAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteProjectAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    // --- Monitoring ---

    [Fact]
    public async Task GetDashboardAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new DashboardOverviewDto()) };
        Assert.NotNull(await api.GetDashboardAsync());
    }

    [Fact]
    public async Task GetServerMetricsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<ServerMetricDto>()) };
        var result = await api.GetServerMetricsAsync(1, 48, Xunit.TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }

    // --- Logs ---

    [Fact]
    public async Task GetTaskLogsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<TaskLogDto>()) };
        var result = await api.GetTaskLogsAsync(5, Xunit.TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }

    // --- Settings ---

    [Fact]
    public async Task GetSettingsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new List<AppSettingDto> { new() { Key = "k", Value = "v" } })
        };
        var result = await api.GetSettingsAsync();
        Assert.Single(result);
    }

    [Fact]
    public async Task UpdateSettingAsync_SendsPut()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        await api.UpdateSettingAsync("key1", "val1");
        Assert.Equal(HttpMethod.Put, h.LastRequest!.Method);
    }

    [Fact]
    public async Task GetSecretsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<SecretDto>()) };
        Assert.Empty(await api.GetSecretsAsync());
    }

    [Fact]
    public async Task CreateSecretAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new SecretDto { Id = 1 }) };
        Assert.NotNull(await api.CreateSecretAsync(new CreateSecretRequest()));
    }

    [Fact]
    public async Task CreateSecretAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateSecretAsync(new CreateSecretRequest()));
    }

    [Fact]
    public async Task DeleteSecretAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteSecretAsync(1));
    }

    // --- Docker ---

    [Fact]
    public async Task GetDockerContainersAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<DockerContainerDto>()) };
        Assert.Empty(await api.GetDockerContainersAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteDockerActionAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.ExecuteDockerActionAsync(1, new DockerActionRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetContainerLogsAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("log line") };
        var result = await api.GetContainerLogsAsync(1, new DockerContainerLogsRequest(), Xunit.TestContext.Current.CancellationToken);
        Assert.Equal("log line", result);
    }

    [Fact]
    public async Task GetContainerLogsAsync_Failure_ReturnsEmpty()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        Assert.Equal(string.Empty, await api.GetContainerLogsAsync(1, new DockerContainerLogsRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetDockerImagesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<DockerImageDto>()) };
        Assert.Empty(await api.GetDockerImagesAsync(1));
    }

    [Fact]
    public async Task PullDockerImageAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.PullDockerImageAsync(1, new DockerPullImageRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveDockerImageAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.RemoveDockerImageAsync(1, "img123", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetComposeStacksAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<DockerComposeStackDto>()) };
        Assert.Empty(await api.GetComposeStacksAsync(1));
    }

    [Fact]
    public async Task ExecuteComposeActionAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.ExecuteComposeActionAsync(1, new DockerComposeActionRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetDockerNetworksAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<DockerNetworkDto>()) };
        Assert.Empty(await api.GetDockerNetworksAsync(1));
    }

    [Fact]
    public async Task GetDockerVolumesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<DockerVolumeDto>()) };
        Assert.Empty(await api.GetDockerVolumesAsync(1));
    }

    [Fact]
    public async Task PruneDockerAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.PruneDockerAsync(1, new DockerPruneRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateDockerResourceLimitsAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.UpdateDockerResourceLimitsAsync(1, new DockerResourceLimitsRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InspectContainerAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.InspectContainerAsync(1, "cid", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetComposeFileAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.GetComposeFileAsync(1, "stack1", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveComposeFileAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.SaveComposeFileAsync(1, new DockerComposeFileSaveRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteShellCommandAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.ExecuteShellCommandAsync(1, new DockerExecRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetContainerEnvVarsAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.GetContainerEnvVarsAsync(1, "cid", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListContainerFilesAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.ListContainerFilesAsync(1, new DockerBrowseRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildImageAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.BuildImageAsync(1, new DockerBuildRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    // --- Server Configuration ---

    [Fact]
    public async Task ExportServerConfigAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("yaml: content") };
        var result = await api.ExportServerConfigAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.Equal("yaml: content", result);
    }

    [Fact]
    public async Task ExportServerConfigAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.Null(await api.ExportServerConfigAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidateServerConfigAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ServerConfigValidationResult()) };
        Assert.NotNull(await api.ValidateServerConfigAsync(1, "yaml", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidateServerConfigAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.ValidateServerConfigAsync(1, "yaml", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PreviewServerConfigAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ServerConfigPreviewDto()) };
        Assert.NotNull(await api.PreviewServerConfigAsync(1, "yaml", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PreviewServerConfigAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.PreviewServerConfigAsync(1, "yaml", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeployServerConfigAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ServerConfigDeployResultDto { TasksCreated = 3 }) };
        var result = await api.DeployServerConfigAsync(1, "yaml", Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(3, result!.TasksCreated);
    }

    [Fact]
    public async Task DeployServerConfigAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.DeployServerConfigAsync(1, "yaml", Xunit.TestContext.Current.CancellationToken));
    }

    // --- Server Sub-Resources ---

    [Fact]
    public async Task GetServerNamesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<string> { "srv1", "srv2" }) };
        var result = await api.GetServerNamesAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetServerProjectsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<ProjectDto>())
        };
        Assert.Empty(await api.GetServerProjectsAsync(1));
    }

    [Fact]
    public async Task GetServerPipelinesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<PipelineDto>()) };
        Assert.Empty(await api.GetServerPipelinesAsync(1));
    }

    [Fact]
    public async Task GetServerVariableLibrariesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<VariableLibraryDto>()) };
        Assert.Empty(await api.GetServerVariableLibrariesAsync(1));
    }

    [Fact]
    public async Task GetServerVaultsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<VaultDto>()) };
        Assert.Empty(await api.GetServerVaultsAsync(1));
    }

    [Fact]
    public async Task GetServerReleasesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<ReleaseDto>()) };
        Assert.Empty(await api.GetServerReleasesAsync(1));
    }

    [Fact]
    public async Task GetServerTasksAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<ServerTaskDto> { Items = [], TotalCount = 0 })
        };
        var result = await api.GetServerTasksAsync(1);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetServerLogsAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<TaskLogDto> { Items = [], TotalCount = 0 })
        };
        var result = await api.GetServerLogsAsync(1);
        Assert.Empty(result.Items);
    }

    // --- Server Modules ---

    [Fact]
    public async Task GetServerModulesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<ServerModuleDto>()) };
        Assert.Empty(await api.GetServerModulesAsync(1));
    }

    [Fact]
    public async Task CreateServerModuleAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ServerModuleDto { Id = 1 }) };
        Assert.NotNull(await api.CreateServerModuleAsync(1, new CreateServerModuleRequest()));
    }

    [Fact]
    public async Task CreateServerModuleAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateServerModuleAsync(1, new CreateServerModuleRequest()));
    }

    [Fact]
    public async Task UpdateServerModuleAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ServerModuleDto { Id = 1 }) };
        Assert.NotNull(await api.UpdateServerModuleAsync(1, 1, new UpdateServerModuleRequest()));
    }

    [Fact]
    public async Task DeleteServerModuleAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteServerModuleAsync(1, 1));
    }

    // --- Server Apps ---

    [Fact]
    public async Task GetServerAppsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<ServerAppDto> { Items = [], TotalCount = 0 })
        };
        Assert.Empty(await api.GetServerAppsAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateServerAppAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ServerAppDto { Id = 1 }) };
        Assert.NotNull(await api.CreateServerAppAsync(1, new CreateServerAppRequest()));
    }

    [Fact]
    public async Task CreateServerAppAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateServerAppAsync(1, new CreateServerAppRequest()));
    }

    [Fact]
    public async Task UpdateServerAppAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ServerAppDto { Id = 1 }) };
        Assert.NotNull(await api.UpdateServerAppAsync(1, 1, new UpdateServerAppRequest()));
    }

    [Fact]
    public async Task DeleteServerAppAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteServerAppAsync(1, 1));
    }

    // --- Project Sub-Resources ---

    [Fact]
    public async Task GetProjectServersAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<ProjectServerDto> { Items = [], TotalCount = 0 })
        };
        Assert.Empty(await api.GetProjectServersAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetProjectTasksAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<ServerTaskDto> { Items = [], TotalCount = 0 })
        };
        Assert.Empty((await api.GetProjectTasksAsync(1, ct: Xunit.TestContext.Current.CancellationToken)).Items);
    }

    [Fact]
    public async Task GetProjectLogsAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<TaskLogDto> { Items = [], TotalCount = 0 })
        };
        Assert.Empty((await api.GetProjectLogsAsync(1, ct: Xunit.TestContext.Current.CancellationToken)).Items);
    }

    [Fact]
    public async Task GetProjectActivityAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<ProjectActivityDto>()) };
        Assert.Empty(await api.GetProjectActivityAsync(1));
    }

    // --- Logs (unmasked) ---

    [Fact]
    public async Task GetTaskLogsUnmaskedAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<TaskLogDto>()) };
        Assert.Empty(await api.GetTaskLogsUnmaskedAsync(5, Xunit.TestContext.Current.CancellationToken));
    }

    // --- Audit ---

    [Fact]
    public async Task GetAuditLogsAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<AuditLogDto> { Items = [], TotalCount = 0 })
        };
        var result = await api.GetAuditLogsAsync(search: "test", action: "Create");
        Assert.Empty(result.Items);
        Assert.Contains("search=test", h.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetAuditActionsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<string> { "Create", "Delete" }) };
        Assert.Equal(2, (await api.GetAuditActionsAsync()).Count);
    }

    [Fact]
    public async Task GetAuditEntityTypesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<string> { "Server" }) };
        Assert.Single(await api.GetAuditEntityTypesAsync());
    }

    // --- Apache ---

    [Fact]
    public async Task GetApacheStateAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ApacheDataDto()) };
        Assert.NotNull(await api.GetApacheStateAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetApacheModulesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<ApacheModuleDto>()) };
        Assert.Empty(await api.GetApacheModulesAsync(1));
    }

    [Fact]
    public async Task GetApacheVirtualHostsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<ApacheVirtualHostDto>()) };
        Assert.Empty(await api.GetApacheVirtualHostsAsync(1));
    }

    [Fact]
    public async Task ExecuteApacheActionAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.ExecuteApacheActionAsync(1, new ApacheActionRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetApacheLogsAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.GetApacheLogsAsync(1, new ApacheLogRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetApacheVHostConfigAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.GetApacheVHostConfigAsync(1, "site1", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveApacheVHostConfigAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.SaveApacheVHostConfigAsync(1, new ApacheVHostSaveRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetApacheHtaccessAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.GetApacheHtaccessAsync(1, "/var/www", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveApacheHtaccessAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.SaveApacheHtaccessAsync(1, new ApacheHtaccessSaveRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    // --- Certbot ---

    [Fact]
    public async Task GetCertbotCertificatesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<CertbotCertificateDto>()) };
        Assert.Empty(await api.GetCertbotCertificatesAsync(1));
    }

    [Fact]
    public async Task ExecuteCertbotActionAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.ExecuteCertbotActionAsync(1, new CertbotActionRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateCertbotCertificateAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.CreateCertbotCertificateAsync(1, new CertbotCreateRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    // --- Module Links ---

    [Fact]
    public async Task GetModuleLinksAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<ModuleLinkDto>()) };
        Assert.Empty(await api.GetModuleLinksAsync(1));
    }

    [Fact]
    public async Task GetLinksForResourceAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<LinkedResourceDto>()) };
        Assert.Empty(await api.GetLinksForResourceAsync(1, ModuleLinkType.Docker, "nginx"));
    }

    [Fact]
    public async Task GetModuleLinksPageAsync_PostsResourceSetAndReturnsPage()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<LinkedResourceDto>
            {
                Items = [new LinkedResourceDto { LinkId = 5, Identifier = "site.conf" }],
                TotalCount = 26,
                Page = 2,
                PageSize = 25
            })
        };
        var request = new ModuleLinkPageRequest
        {
            SourceType = ModuleLinkType.Docker,
            ResourceIdentifiers = ["nginx", "worker"],
            Page = 2,
            PageSize = 25,
            Search = "site"
        };

        var result = await api.GetModuleLinksPageAsync(1, request, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(26, result.TotalCount);
        Assert.Equal("site.conf", Assert.Single(result.Items).Identifier);
        Assert.Equal(HttpMethod.Post, h.LastRequest!.Method);
        Assert.EndsWith("api/servers/1/module-links/resource-page", h.LastRequest.RequestUri!.ToString());
        var sent = await h.LastRequest.Content!.ReadFromJsonAsync<ModuleLinkPageRequest>(Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(sent);
        Assert.Equal(["nginx", "worker"], sent.ResourceIdentifiers);
        Assert.Equal(2, sent.Page);
    }

    [Fact]
    public async Task CreateModuleLinkAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ModuleLinkDto { Id = 1 }) };
        Assert.NotNull(await api.CreateModuleLinkAsync(1, new CreateModuleLinkRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateModuleLinkAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateModuleLinkAsync(1, new CreateModuleLinkRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteModuleLinkAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteModuleLinkAsync(1, 5, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AutoDetectModuleLinksAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.AutoDetectModuleLinksAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    // --- Variable Libraries ---

    [Fact]
    public async Task GetVariableLibrariesAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<VariableLibraryDto> { Items = [], TotalCount = 0 })
        };
        Assert.Empty((await api.GetVariableLibrariesAsync()).Items);
    }

    [Fact]
    public async Task GetVariableLibraryDetailAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new VariableLibraryDetailDto { Id = 1 }) };
        Assert.NotNull(await api.GetVariableLibraryDetailAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetVariableLibraryNamesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<string> { "lib1" }) };
        Assert.Single(await api.GetVariableLibraryNamesAsync());
    }

    [Fact]
    public async Task GetVariableSuggestionKeysAsync_UsesAggregatePagedEndpoint()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<string>
            {
                Items = ["KEY"],
                TotalCount = 201,
                Page = 2,
                PageSize = 200
            })
        };

        var result = await api.GetVariableSuggestionKeysAsync(2, 200, 7, ct: Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(["KEY"], result.Items);
        var uri = h.LastRequest!.RequestUri!.ToString();
        Assert.Contains("api/variable-libraries/suggestion-keys", uri);
        Assert.Contains("page=2", uri);
        Assert.Contains("pageSize=200", uri);
        Assert.Contains("projectId=7", uri);
    }

    [Fact]
    public async Task CreateVariableLibraryAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new VariableLibraryDto { Id = 1 }) };
        Assert.NotNull(await api.CreateVariableLibraryAsync(new CreateVariableLibraryRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateVariableLibraryAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateVariableLibraryAsync(new CreateVariableLibraryRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateVariableLibraryAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new VariableLibraryDto { Id = 1 }) };
        Assert.NotNull(await api.UpdateVariableLibraryAsync(1, new UpdateVariableLibraryRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteVariableLibraryAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteVariableLibraryAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateVariableEntryAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new VariableEntryDto { Id = 1 }) };
        Assert.NotNull(await api.CreateVariableEntryAsync(1, new CreateVariableEntryRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateVariableEntryAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new VariableEntryDto { Id = 1 }) };
        Assert.NotNull(await api.UpdateVariableEntryAsync(1, 1, new UpdateVariableEntryRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteVariableEntryAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteVariableEntryAsync(1, 1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetVariableEntryVersionsPageAsync_ReturnsPage()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<VariableEntryVersionDto> { TotalCount = 51 })
        };
        var result = await api.GetVariableEntryVersionsPageAsync(
            1, 2, 2, 25, sortBy: "Version", sortDescending: true,
            ct: Xunit.TestContext.Current.CancellationToken);
        Assert.Equal(51, result.TotalCount);
        Assert.Contains("api/variable-libraries/1/entries/2/versions", h.LastRequest!.RequestUri!.ToString());
        Assert.Contains("page=2", h.LastRequest.RequestUri.ToString());
        Assert.Contains("sortDescending=true", h.LastRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task ExportVariableEntriesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<VariableEntryDto>()) };
        Assert.Empty(await api.ExportVariableEntriesAsync(1));
    }

    [Fact]
    public async Task ImportVariableEntriesAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ImportResultDto()) };
        Assert.NotNull(await api.ImportVariableEntriesAsync(1, [], Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportVariableEntriesAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.ImportVariableEntriesAsync(1, [], Xunit.TestContext.Current.CancellationToken));
    }

    // --- Vaults ---

    [Fact]
    public async Task GetVaultsAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<VaultDto> { Items = [], TotalCount = 0 })
        };
        Assert.Empty((await api.GetVaultsAsync()).Items);
    }

    [Fact]
    public async Task GetVaultDetailAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new VaultDetailDto { Id = 1 }) };
        Assert.NotNull(await api.GetVaultDetailAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetVaultNamesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<string> { "vault1" }) };
        Assert.Single(await api.GetVaultNamesAsync());
    }

    [Fact]
    public async Task CreateVaultAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new VaultDto { Id = 1 }) };
        Assert.NotNull(await api.CreateVaultAsync(new CreateVaultRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateVaultAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateVaultAsync(new CreateVaultRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateVaultAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new VaultDto { Id = 1 }) };
        Assert.NotNull(await api.UpdateVaultAsync(1, new UpdateVaultRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteVaultAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteVaultAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateVaultSecretAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new VaultSecretDto { Key = "k" }) };
        Assert.NotNull(await api.CreateVaultSecretAsync(1, new CreateVaultSecretRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateVaultSecretAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new VaultSecretDto { Key = "k" }) };
        Assert.NotNull(await api.UpdateVaultSecretAsync(1, 1, new UpdateVaultSecretRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteVaultSecretAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteVaultSecretAsync(1, 1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetVaultSecretVersionsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<VaultSecretVersionDto>()) };
        Assert.Empty(await api.GetVaultSecretVersionsAsync(1, 1));
    }

    [Fact]
    public async Task ExportVaultSecretKeysAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<string> { "secret-key" }) };
        Assert.Single(await api.ExportVaultSecretKeysAsync(1));
    }

    [Fact]
    public async Task ImportVaultSecretsAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ImportResultDto()) };
        Assert.NotNull(await api.ImportVaultSecretsAsync(1, [], Xunit.TestContext.Current.CancellationToken));
    }

    // --- Pipeline Templates ---

    [Fact]
    public async Task DryRunPipelineAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new DryRunResultDto()) };
        Assert.NotNull(await api.DryRunPipelineAsync(1, ct: Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DryRunPipelineAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.DryRunPipelineAsync(1, ct: Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPipelineTemplatesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new List<PipelineTemplateSummaryDto>())
        };
        Assert.Empty(await api.GetPipelineTemplatesAsync());
    }

    [Fact]
    public async Task GetPipelineTemplatesAsync_CachesResult()
    {
        var (api, h) = Create();
        var callCount = 0;
        h.ResponseFactory = _ => { callCount++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<PipelineTemplateSummaryDto>()) }; };

        await api.GetPipelineTemplatesAsync();
        await api.GetPipelineTemplatesAsync();
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task InvalidateTemplateCache_ClearsCachedTemplates()
    {
        var (api, h) = Create();
        var callCount = 0;
        h.ResponseFactory = _ => { callCount++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<PipelineTemplateSummaryDto>()) }; };

        await api.GetPipelineTemplatesAsync();
        api.InvalidateTemplateCache();
        await api.GetPipelineTemplatesAsync();
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetPipelineTemplateAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PipelineTemplateDto { Id = 1 }) };
        Assert.NotNull(await api.GetPipelineTemplateAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPipelineTemplateVersionsAsync_UsesPagedMetadataEndpoint()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<PipelineTemplateVersionSummaryDto>
            {
                Items = [new PipelineTemplateVersionSummaryDto { Version = 11 }],
                TotalCount = 21
            })
        };

        var result = await api.GetPipelineTemplateVersionsAsync(
            1, 2, 10, "Version", true, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(21, result.TotalCount);
        var uri = h.LastRequest!.RequestUri!.ToString();
        Assert.Contains("api/pipelines/templates/1/versions", uri);
        Assert.Contains("page=2", uri);
        Assert.Contains("sortDescending=true", uri);
    }

    [Fact]
    public async Task GetPipelineTemplateVersionAsync_LoadsOnlyRequestedYaml()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PipelineTemplateVersionDto { Version = 7, YamlContent = "name: seven" })
        };

        var result = await api.GetPipelineTemplateVersionAsync(1, 7, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal("name: seven", result!.YamlContent);
        Assert.EndsWith("api/pipelines/templates/1/versions/7", h.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task CreatePipelineTemplateAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PipelineTemplateDto { Id = 1 }) };
        Assert.NotNull(await api.CreatePipelineTemplateAsync(new CreatePipelineTemplateRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdatePipelineTemplateAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PipelineTemplateDto { Id = 1 }) };
        Assert.NotNull(await api.UpdatePipelineTemplateAsync(1, new UpdatePipelineTemplateRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletePipelineTemplateAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeletePipelineTemplateAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExportPipelineTemplateAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        var result = await api.ExportPipelineTemplateAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(3, result!.Length);
    }

    [Fact]
    public async Task ExportPipelineTemplateAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.Null(await api.ExportPipelineTemplateAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportPipelineTemplateAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PipelineTemplateDto { Id = 2 }) };
        Assert.NotNull(await api.ImportPipelineTemplateAsync([1, 2], "template.json", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidatePipelineYamlAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PipelineYamlDefinition()) };
        Assert.NotNull(await api.ValidatePipelineYamlAsync("stages: []", Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidatePipelineYamlAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.ValidatePipelineYamlAsync("bad yaml", Xunit.TestContext.Current.CancellationToken));
    }

    // --- Releases ---

    [Fact]
    public async Task GetReleasesAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<ReleaseDto> { Items = [], TotalCount = 0 })
        };
        Assert.Empty((await api.GetReleasesAsync()).Items);
    }

    [Fact]
    public async Task GetReleaseAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ReleaseDto { Id = 1 }) };
        Assert.NotNull(await api.GetReleaseAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SyncReleasesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<ReleaseDto>()) };
        Assert.Empty(await api.SyncReleasesAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TriggerReleaseBuildAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ReleaseDto { Id = 1 }) };
        Assert.NotNull(await api.TriggerReleaseBuildAsync(1, new TriggerReleaseBuildRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TriggerReleaseBuildAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.TriggerReleaseBuildAsync(1, new TriggerReleaseBuildRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RollbackReleaseAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ReleaseDto { Id = 1 }) };
        Assert.NotNull(await api.RollbackReleaseAsync(1, new RollbackReleaseRequest { PipelineId = 1 }, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PromoteReleaseAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new ReleaseDto { Id = 1 }) };
        Assert.NotNull(await api.PromoteReleaseAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    // --- Admin: Users ---

    [Fact]
    public async Task GetUsersAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<UserDto> { Items = [], TotalCount = 0 })
        };
        Assert.Empty((await api.GetUsersAsync()).Items);
    }

    [Fact]
    public async Task GetUserDetailAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new UserDto { Id = 1 }) };
        Assert.NotNull(await api.GetUserDetailAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCurrentUserAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new UserDto { Id = 1 }) };
        Assert.NotNull(await api.GetCurrentUserAsync(Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateUserAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new UserDto { Id = 1 }) };
        Assert.NotNull(await api.CreateUserAsync(new CreateUserRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateUserAsync_Failure()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateUserAsync(new CreateUserRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateUserAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new UserDto { Id = 1 }) };
        Assert.NotNull(await api.UpdateUserAsync(1, new UpdateUserRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteUserAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteUserAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChangeUserPasswordAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.ChangeUserPasswordAsync(1, new ChangeUserPasswordRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.NoContent);
        Assert.True(await api.ChangeOwnPasswordAsync(new ChangeUserPasswordRequest { CurrentPassword = "old", NewPassword = "newpass123" }, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_BadRequest_ReturnsFalse()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.False(await api.ChangeOwnPasswordAsync(new ChangeUserPasswordRequest { CurrentPassword = "wrong", NewPassword = "newpass123" }, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetRolesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<string> { "Admin" }) };
        Assert.Single(await api.GetRolesAsync());
    }

    [Fact]
    public async Task GetRoleDtosAsync_ReturnsPaginatedResultAndBuildsQuery()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<RoleDto>
            {
                Items = [new RoleDto { Id = 1, Name = "Admin" }],
                TotalCount = 26,
                Page = 2,
                PageSize = 25
            })
        };

        var result = await api.GetRoleDtosAsync(2, 25, "adm", "Name", true);

        Assert.Single(result.Items);
        Assert.Equal(26, result.TotalCount);
        var uri = h.LastRequest!.RequestUri!.ToString();
        Assert.Contains("page=2", uri);
        Assert.Contains("pageSize=25", uri);
        Assert.Contains("search=adm", uri);
        Assert.Contains("sortBy=Name", uri);
        Assert.Contains("sortDescending=true", uri);
    }

    [Fact]
    public async Task GetRoleAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new RoleDto { Id = 1, Name = "Admin" }) };
        var result = await api.GetRoleAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetRoleUsersAsync_BuildsServerPaginationQuery()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<RoleUserDto>())
        };

        await api.GetRoleUsersAsync(4, 3, 25, "ali", "Username", true, Xunit.TestContext.Current.CancellationToken);

        var uri = h.LastRequest!.RequestUri!.ToString();
        Assert.Contains("api/roles/4/users", uri);
        Assert.Contains("page=3", uri);
        Assert.Contains("pageSize=25", uri);
        Assert.Contains("search=ali", uri);
        Assert.Contains("sortBy=Username", uri);
        Assert.Contains("sortDescending=true", uri);
    }

    [Fact]
    public async Task GetUsersAvailableForRoleAsync_UsesDedicatedPagedEndpoint()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<RoleUserDto>())
        };

        await api.GetUsersAvailableForRoleAsync(
            4, 2, 10, "bob", "Username", ct: Xunit.TestContext.Current.CancellationToken);

        var uri = h.LastRequest!.RequestUri!.ToString();
        Assert.Contains("api/roles/4/available-users", uri);
        Assert.Contains("page=2", uri);
        Assert.Contains("pageSize=10", uri);
        Assert.Contains("search=bob", uri);
        Assert.DoesNotContain("api/users?pageSize=200", uri);
    }

    [Fact]
    public async Task CreateRoleAsync_Success_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new RoleDto { Id = 1, Name = "New" }) };
        var result = await api.CreateRoleAsync(new CreateRoleRequest { Name = "New" }, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateRoleAsync_Failure_ReturnsNull()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateRoleAsync(new CreateRoleRequest { Name = "x" }, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateRoleAsync_Success_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new RoleDto { Id = 1, Name = "Updated" }) };
        var result = await api.UpdateRoleAsync(1, new UpdateRoleRequest { Name = "Updated" }, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DeleteRoleAsync_Success_ReturnsTrue()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteRoleAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetRolePermissionsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<ResourcePermissionDto>()) };
        var result = await api.GetRolePermissionsAsync(1);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SetRolePermissionsAsync_Success_ReturnsTrue()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.SetRolePermissionsAsync(1, new SetResourcePermissionsRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CloneRoleAsync_Success_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new RoleDto { Id = 2, Name = "Clone" }) };
        var result = await api.CloneRoleAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CloneRoleAsync_Failure_ReturnsNull()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.Null(await api.CloneRoleAsync(99, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetUserEffectivePermissionsAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new UserPermissionSummaryDto()) };
        var result = await api.GetUserEffectivePermissionsAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetMyPermissionsAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new UserPermissionSummaryDto()) };
        var result = await api.GetMyPermissionsAsync(Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    // --- Admin: Dashboards ---

    [Fact]
    public async Task GetDashboardsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<DashboardDto>()) };
        Assert.Empty(await api.GetDashboardsAsync());
    }

    [Fact]
    public async Task GetAdminDashboardAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new DashboardDto { Id = 1 }) };
        Assert.NotNull(await api.GetDashboardAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateDashboardAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new DashboardDto { Id = 1 }) };
        Assert.NotNull(await api.CreateDashboardAsync(new CreateDashboardRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateDashboardAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new DashboardDto { Id = 1 }) };
        Assert.NotNull(await api.UpdateDashboardAsync(1, new UpdateDashboardRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteDashboardAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteDashboardAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    // --- Admin: Plugins ---

    [Fact]
    public async Task GetPluginsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<PluginRegistrationDto> { Items = [], TotalCount = 0 })
        };
        Assert.Empty(await api.GetPluginsAsync(Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPluginAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PluginRegistrationDto { Id = 1 }) };
        Assert.NotNull(await api.GetPluginAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterPluginAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PluginRegistrationDto { Id = 1 }) };
        Assert.NotNull(await api.RegisterPluginAsync(new RegisterPluginRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdatePluginAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PluginRegistrationDto { Id = 1 }) };
        Assert.NotNull(await api.UpdatePluginAsync(1, new UpdatePluginRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnregisterPluginAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.UnregisterPluginAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    // --- Admin: Alerts ---

    [Fact]
    public async Task GetAlertRulesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new List<AlertRuleDto>()) };
        Assert.Empty(await api.GetAlertRulesAsync());
    }

    [Fact]
    public async Task CreateAlertRuleAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new AlertRuleDto { Id = 1 }) };
        Assert.NotNull(await api.CreateAlertRuleAsync(new CreateAlertRuleRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateAlertRuleAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new AlertRuleDto { Id = 1 }) };
        Assert.NotNull(await api.UpdateAlertRuleAsync(1, new UpdateAlertRuleRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAlertRuleAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteAlertRuleAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    // --- Admin: Notifications ---

    [Fact]
    public async Task GetNotificationChannelsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<NotificationChannelDto>())
        };
        Assert.Empty(await api.GetNotificationChannelsAsync());
    }

    [Fact]
    public async Task CreateNotificationChannelAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new NotificationChannelDto { Id = 1 }) };
        Assert.NotNull(await api.CreateNotificationChannelAsync(new CreateNotificationChannelRequest(), Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteNotificationChannelAsync_Success()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteNotificationChannelAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    // --- Git Light Repos ---

    [Fact]
    public async Task GetGitReposAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<GitLightRepoDto>
            {
                Items = [new GitLightRepoDto { Id = 1, Name = "repo" }],
                TotalCount = 1
            })
        };
        var result = await api.GetGitReposAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetGitRepoAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new GitLightRepoDto { Id = 1, Name = "repo" })
        };
        var result = await api.GetGitRepoAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("repo", result!.Name);
    }

    [Fact]
    public async Task CreateGitRepoAsync_Success_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new GitLightRepoDto { Id = 1, Name = "new" })
        };
        var result = await api.CreateGitRepoAsync(new CreateGitLightRepoRequest { Name = "new" }, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateGitRepoAsync_Failure_ReturnsNull()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        var result = await api.CreateGitRepoAsync(new CreateGitLightRepoRequest { Name = "x" }, Xunit.TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateGitRepoAsync_Success_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new GitLightRepoDto { Id = 1, Name = "updated" })
        };
        var result = await api.UpdateGitRepoAsync(1, new UpdateGitLightRepoRequest { Description = "updated" }, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpdateGitRepoAsync_Failure_ReturnsNull()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var result = await api.UpdateGitRepoAsync(1, new UpdateGitLightRepoRequest { Description = "x" }, Xunit.TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteGitRepoAsync_Success_ReturnsTrue()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteGitRepoAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteGitRepoAsync_Failure_ReturnsFalse()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.False(await api.DeleteGitRepoAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    // --- Git Light Branches ---

    [Fact]
    public async Task GetGitBranchesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<GitLightBranchDto>
            {
                Items = [new GitLightBranchDto { Name = "main" }],
                TotalCount = 1
            })
        };
        var result = await api.GetGitBranchesAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task CreateGitBranchAsync_Success_ReturnsTrue()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.CreateGitBranchAsync(1, new CreateGitLightBranchRequest { Name = "feat", StartRef = "main" }, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteGitBranchAsync_Success_ReturnsTrue()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteGitBranchAsync(1, "feat", Xunit.TestContext.Current.CancellationToken));
    }

    // --- Git Light Tags ---

    [Fact]
    public async Task GetGitTagsAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<GitLightTagDto>
            {
                Items = [new GitLightTagDto { Name = "v1.0" }],
                TotalCount = 1
            })
        };
        var result = await api.GetGitTagsAsync(1, Xunit.TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task CreateGitTagAsync_Success_ReturnsTrue()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.CreateGitTagAsync(1, new CreateGitLightTagRequest { Name = "v1.0", Ref = "main" }, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteGitTagAsync_Success_ReturnsTrue()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteGitTagAsync(1, "v1.0", Xunit.TestContext.Current.CancellationToken));
    }

    // --- Git Light File Browser ---

    [Fact]
    public async Task GetGitTreeAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<GitLightTreeEntryDto>
            {
                Items = [new GitLightTreeEntryDto { Name = "file.txt" }],
                TotalCount = 1
            })
        };
        var result = await api.GetGitTreeAsync(1, "main", "src", Xunit.TestContext.Current.CancellationToken);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetGitBlobAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new GitLightBlobDto { Content = "hello" })
        };
        var result = await api.GetGitBlobAsync(1, "main", "file.txt", Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetGitBlameAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new List<GitLightBlameLine> { new() { LineNumber = 1 } })
        };
        var result = await api.GetGitBlameAsync(1, "main", "file.txt");
        Assert.Single(result);
    }

    // --- Git Light Commits ---

    [Fact]
    public async Task GetGitCommitsAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<GitLightCommitDto> { Items = [new() { Sha = "abc" }], TotalCount = 1 })
        };
        var result = await api.GetGitCommitsAsync(1, "main", 1, 10, "search");
        Assert.Single(result.Items);
    }

    // --- Git Light Pull Requests ---

    [Fact]
    public async Task GetGitPullRequestsAsync_ReturnsPaginated()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<InternalPullRequestDto> { Items = [new() { Number = 1 }], TotalCount = 1 })
        };
        var result = await api.GetGitPullRequestsAsync(1, status: PullRequestStatus.Open);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetGitPullRequestAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new InternalPullRequestDto { Number = 1 }) };
        var result = await api.GetGitPullRequestAsync(1, 1, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateGitPullRequestAsync_Success_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new InternalPullRequestDto { Number = 1 }) };
        var result = await api.CreateGitPullRequestAsync(1, new CreateInternalPullRequestRequest { Title = "PR" }, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task CreateGitPullRequestAsync_Failure_ReturnsNull()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        Assert.Null(await api.CreateGitPullRequestAsync(1, new CreateInternalPullRequestRequest { Title = "x" }, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MergeGitPullRequestAsync_Success_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new InternalPullRequestDto { Number = 1 }) };
        var result = await api.MergeGitPullRequestAsync(1, 1, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task MergeGitPullRequestAsync_Failure_ReturnsNull()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.Conflict);
        Assert.Null(await api.MergeGitPullRequestAsync(1, 1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CloseGitPullRequestAsync_Success_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new InternalPullRequestDto { Number = 1 }) };
        var result = await api.CloseGitPullRequestAsync(1, 1, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetGitPullRequestDiffAsync_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new PullRequestDiffDto()) };
        var result = await api.GetGitPullRequestDiffAsync(1, 1, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    // --- Git Branch Protection ---

    [Fact]
    public async Task GetGitCommitGraphAsync_ReturnsString()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("graph-data") };
        var result = await api.GetGitCommitGraphAsync(1, ct: Xunit.TestContext.Current.CancellationToken);
        Assert.Equal("graph-data", result);
    }

    [Fact]
    public async Task GetGitBranchProtectionRulesAsync_ReturnsList()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json(new PaginatedResult<BranchProtectionRuleDto>
            {
                Items = [new BranchProtectionRuleDto { Id = 1 }],
                TotalCount = 1
            })
        };
        Assert.Single(await api.GetGitBranchProtectionRulesAsync(1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateGitBranchProtectionRuleAsync_Success_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new BranchProtectionRuleDto { Id = 1 }) };
        var result = await api.CreateGitBranchProtectionRuleAsync(1, new CreateBranchProtectionRuleRequest { Pattern = "main" }, Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task UpdateGitBranchProtectionRuleAsync_Success_ReturnsDto()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = Json(new BranchProtectionRuleDto { Id = 1 }) };
        var result = await api.UpdateGitBranchProtectionRuleAsync(1, 1, new UpdateBranchProtectionRuleRequest(), Xunit.TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DeleteGitBranchProtectionRuleAsync_Success_ReturnsTrue()
    {
        var (api, h) = Create();
        h.Response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.True(await api.DeleteGitBranchProtectionRuleAsync(1, 1, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAllProjectsAsync_AggregatesEveryServerPage()
    {
        var (api, h) = Create();
        h.ResponseFactory = request =>
        {
            var secondPage = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal);
            var items = secondPage
                ? Enumerable.Range(101, 5).Select(id => new ProjectDto { Id = id, Name = $"Project {id}" }).ToList()
                : Enumerable.Range(1, 100).Select(id => new ProjectDto { Id = id, Name = $"Project {id}" }).ToList();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(new PaginatedResult<ProjectDto> { Items = items, TotalCount = 105 })
            };
        };

        var result = await api.GetAllProjectsAsync();

        Assert.Equal(105, result.Count);
        Assert.Contains(h.RequestUris, uri => uri.Contains("page=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAllServersAsync_AggregatesEveryServerPage()
    {
        var (api, h) = Create();
        h.ResponseFactory = request =>
        {
            var secondPage = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal);
            var items = secondPage
                ? Enumerable.Range(101, 5).Select(id => new ServerDto { Id = id, Name = $"Server {id}" }).ToList()
                : Enumerable.Range(1, 100).Select(id => new ServerDto { Id = id, Name = $"Server {id}" }).ToList();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(new PaginatedResult<ServerDto> { Items = items, TotalCount = 105 })
            };
        };

        var result = await api.GetAllServersAsync();

        Assert.Equal(105, result.Count);
        Assert.Contains(h.RequestUris, uri => uri.Contains("page=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAllVariableLibrariesAsync_AggregatesEveryServerPage()
    {
        var (api, h) = Create();
        h.ResponseFactory = request =>
        {
            var secondPage = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal);
            var items = secondPage
                ? Enumerable.Range(101, 5).Select(id => new VariableLibraryDto { Id = id, Name = $"Library {id}" }).ToList()
                : Enumerable.Range(1, 100).Select(id => new VariableLibraryDto { Id = id, Name = $"Library {id}" }).ToList();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Json(new PaginatedResult<VariableLibraryDto> { Items = items, TotalCount = 105 })
            };
        };

        var result = await api.GetAllVariableLibrariesAsync();

        Assert.Equal(105, result.Count);
        Assert.Contains(h.RequestUris, uri => uri.Contains("page=2", StringComparison.Ordinal));
    }

    // --- Test Handler ---

    private sealed class TestHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public HttpRequestMessage? LastRequest { get; private set; }
        public List<string> RequestUris { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            if (ResponseFactory is not null)
                return Task.FromResult(ResponseFactory(request));
            return Task.FromResult(Response);
        }
    }
}
