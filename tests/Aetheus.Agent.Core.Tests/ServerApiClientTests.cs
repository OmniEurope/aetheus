// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.DTOs;
using NSubstitute;

namespace Aetheus.Agent.Core.Tests;

public class ServerApiClientTests
{
    private readonly ServerApiClient _sut;
    private readonly MockHttpMessageHandler _handler;

    public ServerApiClientTests()
    {
        _handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:5301/") };

        var factoryMock = Substitute.For<IHttpClientFactory>();
        factoryMock.CreateClient("AetheusServer").Returns(httpClient);

        _sut = new ServerApiClient(factoryMock);
    }

    [Fact]
    public async Task RegisterAsync_Success_ReturnsResponse()
    {
        var expected = new ServerRegistrationResponse { ServerId = 42, BearerToken = "tok" };
        _handler.SetResponse(HttpStatusCode.OK, expected);

        var result = await _sut.RegisterAsync(
            new ServerRegistrationRequest { Hostname = "test" },
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(42, result.ServerId);
        Assert.Equal("tok", result.BearerToken);
        AssertRequest(HttpMethod.Post, "/api/auth/register");
        Assert.Equal("test", DeserializeBody<ServerRegistrationRequest>().Hostname);
    }

    [Fact]
    public async Task RegisterAsync_Failure_ThrowsHttpRequestException()
    {
        _handler.SetResponse(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => _sut.RegisterAsync(
                new ServerRegistrationRequest { Hostname = "test" },
                TestContext.Current.CancellationToken));
        AssertRequest(HttpMethod.Post, "/api/auth/register");
    }

    [Fact]
    public async Task GetPendingTasksAsync_Success_ReturnsTasks()
    {
        var tasks = new List<PendingTaskDto>
        {
            new() { Id = 1, Name = "Deploy" }
        };
        _handler.SetResponse(HttpStatusCode.OK, tasks);

        var result = await _sut.GetPendingTasksAsync(1, 2, TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("Deploy", result[0].Name);
        AssertRequest(HttpMethod.Post, "/api/tasks/claim?serverId=1&take=2");
        Assert.Empty(JsonDocument.Parse(_handler.CapturedBody!).RootElement.EnumerateObject());
    }

    [Fact]
    public async Task GetPendingTasksAsync_Failure_ThrowsHttpRequestException()
    {
        _handler.SetResponse(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => _sut.GetPendingTasksAsync(1, 2, TestContext.Current.CancellationToken));
        AssertRequest(HttpMethod.Post, "/api/tasks/claim?serverId=1&take=2");
    }

    [Fact]
    public async Task SendHeartbeatAsync_Success_PostsToHeartbeatRoute()
    {
        _handler.SetResponse(HttpStatusCode.OK);

        await _sut.SendHeartbeatAsync(
            1, new ServerHeartbeatDto { CpuPercent = 12.5 }, TestContext.Current.CancellationToken);

        AssertRequest(HttpMethod.Post, "/api/servers/1/heartbeat");
        Assert.Equal(12.5, DeserializeBody<ServerHeartbeatDto>().CpuPercent);
    }

    [Fact]
    public async Task AppendLogAsync_Success_PostsToLogRoute()
    {
        _handler.SetResponse(HttpStatusCode.OK);

        await _sut.AppendLogAsync(
            new AppendLogRequest { TaskId = 1, Message = "test" },
            TestContext.Current.CancellationToken);

        AssertRequest(HttpMethod.Post, "/api/logs");
        var body = DeserializeBody<AppendLogRequest>();
        Assert.Equal(1, body.TaskId);
        Assert.Equal("test", body.Message);
    }

    [Fact]
    public async Task AppendLogBatchAsync_Success_PostsToBatchRoute()
    {
        _handler.SetResponse(HttpStatusCode.OK);

        await _sut.AppendLogBatchAsync(
            [new AppendLogRequest { TaskId = 1, Message = "test" }],
            TestContext.Current.CancellationToken);

        AssertRequest(HttpMethod.Post, "/api/logs/batch");
        var body = DeserializeBody<List<AppendLogRequest>>();
        var log = Assert.Single(body);
        Assert.Equal(1, log.TaskId);
        Assert.Equal("test", log.Message);
    }

    private void AssertRequest(HttpMethod method, string pathAndQuery)
    {
        Assert.Equal(method, _handler.CapturedMethod);
        Assert.Equal(pathAndQuery, _handler.CapturedPathAndQuery);
        Assert.NotNull(_handler.CapturedBody);
    }

    private T DeserializeBody<T>()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return JsonSerializer.Deserialize<T>(_handler.CapturedBody!, options)!;
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private object? _responseBody;

        public HttpMethod? CapturedMethod { get; private set; }
        public string? CapturedPathAndQuery { get; private set; }
        public string? CapturedBody { get; private set; }

        public void SetResponse(HttpStatusCode statusCode, object? body = null)
        {
            _statusCode = statusCode;
            _responseBody = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedMethod = request.Method;
            CapturedPathAndQuery = request.RequestUri?.PathAndQuery;
            CapturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(_statusCode);
            if (_responseBody is not null)
                response.Content = JsonContent.Create(_responseBody);
            return response;
        }
    }
}
