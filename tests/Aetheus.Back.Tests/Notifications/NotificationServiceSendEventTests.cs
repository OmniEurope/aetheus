// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class NotificationServiceSendEventTests
{
    private readonly INotificationRepository _repo = Substitute.For<INotificationRepository>();
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly IHttpClientFactory _httpFactory = Substitute.For<IHttpClientFactory>();
    private readonly NotificationService _sut;

    public NotificationServiceSendEventTests()
    {
        _sut = new NotificationService(
            _repo, _audit,
            _httpFactory,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<NotificationService>>(),
            TimeProvider.System);
    }

    [Fact]
    public async Task SendEventAsync_NoRules_DoesNothing()
    {
        _repo.GetRulesForEventAsync("test.event", Arg.Any<CancellationToken>())
            .Returns([]);

        await _sut.SendEventAsync("test.event", new { Data = "value" }, ct: TestContext.Current.CancellationToken);

        _httpFactory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task SendEventAsync_EmailChannel_LogsOnly()
    {
        var channel = new NotificationChannel
        {
            Id = 1,
            Name = "email",
            Type = NotificationChannelType.Email,
            ConfigurationJson = "{}",
            Rules = []
        };
        var rule = new NotificationRule
        {
            Id = 1,
            EventType = "test",
            Channel = channel,
            NotificationChannelId = 1
        };
        _repo.GetRulesForEventAsync("test", Arg.Any<CancellationToken>())
            .Returns([rule]);

        await _sut.SendEventAsync("test", new { }, ct: TestContext.Current.CancellationToken);

        _httpFactory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task SendEventAsync_SlackChannel_PostsToWebhookUrl()
    {
        var handler = new FakeHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://93.184.216.34/") };
        _httpFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var channel = new NotificationChannel
        {
            Id = 1,
            Name = "slack",
            Type = NotificationChannelType.Slack,
            ConfigurationJson = """{"webhookUrl":"http://93.184.216.34/slack"}""",
            Rules = []
        };
        var rule = new NotificationRule
        {
            Id = 1,
            EventType = "deploy",
            Channel = channel,
            NotificationChannelId = 1
        };
        _repo.GetRulesForEventAsync("deploy", Arg.Any<CancellationToken>())
            .Returns([rule]);

        await _sut.SendEventAsync("deploy", new { Version = "1.0" }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("http://93.184.216.34/slack", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendEventAsync_TeamsChannel_PostsToWebhookUrl()
    {
        var handler = new FakeHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://93.184.216.34/") };
        _httpFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var channel = new NotificationChannel
        {
            Id = 1,
            Name = "teams",
            Type = NotificationChannelType.Teams,
            ConfigurationJson = """{"webhookUrl":"http://93.184.216.34/teams"}""",
            Rules = []
        };
        var rule = new NotificationRule
        {
            Id = 1,
            EventType = "build",
            Channel = channel,
            NotificationChannelId = 1
        };
        _repo.GetRulesForEventAsync("build", Arg.Any<CancellationToken>())
            .Returns([rule]);

        await _sut.SendEventAsync("build", new { Status = "success" }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("http://93.184.216.34/teams", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendEventAsync_WebhookChannel_PostsWithHeaders()
    {
        var handler = new FakeHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://93.184.216.34/") };
        _httpFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var channel = new NotificationChannel
        {
            Id = 1,
            Name = "hook",
            Type = NotificationChannelType.Webhook,
            ConfigurationJson = """{"url":"http://93.184.216.34/hook","secret":"mysecret"}""",
            Rules = []
        };
        var rule = new NotificationRule
        {
            Id = 1,
            EventType = "alert",
            Channel = channel,
            NotificationChannelId = 1
        };
        _repo.GetRulesForEventAsync("alert", Arg.Any<CancellationToken>())
            .Returns([rule]);

        await _sut.SendEventAsync("alert", new { Level = "critical" }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.Contains("X-Aetheus-Event"));
        Assert.True(handler.LastRequest.Headers.Contains("X-Aetheus-Signature"));
    }

    [Fact]
    public async Task SendEventAsync_WebhookChannelNoSecret_NoSignatureHeader()
    {
        var handler = new FakeHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://93.184.216.34/") };
        _httpFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var channel = new NotificationChannel
        {
            Id = 1,
            Name = "hook",
            Type = NotificationChannelType.Webhook,
            ConfigurationJson = """{"url":"http://93.184.216.34/hook"}""",
            Rules = []
        };
        var rule = new NotificationRule
        {
            Id = 1,
            EventType = "evt",
            Channel = channel,
            NotificationChannelId = 1
        };
        _repo.GetRulesForEventAsync("evt", Arg.Any<CancellationToken>())
            .Returns([rule]);

        await _sut.SendEventAsync("evt", new { }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest!.Headers.Contains("X-Aetheus-Event"));
        Assert.False(handler.LastRequest.Headers.Contains("X-Aetheus-Signature"));
    }

    [Fact]
    public async Task SendEventAsync_SlackNoWebhookUrl_DoesNotPost()
    {
        var channel = new NotificationChannel
        {
            Id = 1,
            Name = "slack-no-url",
            Type = NotificationChannelType.Slack,
            ConfigurationJson = """{"other":"value"}""",
            Rules = []
        };
        var rule = new NotificationRule
        {
            Id = 1,
            EventType = "test",
            Channel = channel,
            NotificationChannelId = 1
        };
        _repo.GetRulesForEventAsync("test", Arg.Any<CancellationToken>())
            .Returns([rule]);

        await _sut.SendEventAsync("test", new { }, ct: TestContext.Current.CancellationToken);

        _httpFactory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateRuleAsync_Found_UpdatesAndReturns()
    {
        var rule = new NotificationRule
        {
            Id = 1,
            EventType = "old",
            NotificationChannelId = 1
        };
        _repo.FindRuleAsync(1, Arg.Any<CancellationToken>()).Returns(rule);

        var result = await _sut.UpdateRuleAsync(1, new Aetheus.Shared.DTOs.UpdateNotificationRuleRequest
        {
            EventType = "new.event",
            FilterJson = "{\"filter\":true}",
            IsEnabled = false
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("new.event", result!.EventType);
    }

    [Fact]
    public async Task UpdateRuleAsync_NotFound_ReturnsNull()
    {
        _repo.FindRuleAsync(99, Arg.Any<CancellationToken>())
            .Returns((NotificationRule?)null);

        Assert.Null(await _sut.UpdateRuleAsync(99, new Aetheus.Shared.DTOs.UpdateNotificationRuleRequest(), ct: TestContext.Current.CancellationToken));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
