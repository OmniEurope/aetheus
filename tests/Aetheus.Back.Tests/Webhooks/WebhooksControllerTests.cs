// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class WebhooksControllerTests
{
    private readonly IWebhookService _serviceMock = Substitute.For<IWebhookService>();
    private readonly WebhooksController _sut;

    public WebhooksControllerTests()
    {
        _sut = new WebhooksController(_serviceMock);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin")], "test"))
            }
        };
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsOk()
    {
        _serviceMock.GetSubscriptionsAsync(Arg.Any<CancellationToken>())
            .Returns([new WebhookSubscriptionDto { Id = 1, EventType = "pipeline.completed" }]);

        var result = await _sut.GetSubscriptions(TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single((List<WebhookSubscriptionDto>)ok.Value!);
    }

    [Fact]
    public async Task GetSubscription_Found_ReturnsOk()
    {
        _serviceMock.GetSubscriptionAsync(1, Arg.Any<CancellationToken>())
            .Returns(new WebhookSubscriptionDto { Id = 1, EventType = "pipeline.completed" });

        var result = await _sut.GetSubscription(1, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetSubscription_NotFound_ReturnsNotFound()
    {
        _serviceMock.GetSubscriptionAsync(999, Arg.Any<CancellationToken>())
            .Returns((WebhookSubscriptionDto?)null);

        var result = await _sut.GetSubscription(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateSubscription_ReturnsCreated()
    {
        _serviceMock.CreateSubscriptionAsync(Arg.Any<CreateWebhookSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookSubscriptionDto { Id = 1, EventType = "pipeline.completed" });

        var result = await _sut.CreateSubscription(new CreateWebhookSubscriptionRequest { EventType = "pipeline.completed", TargetUrl = "https://example.com/hook" }, TestContext.Current.CancellationToken);

        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task UpdateSubscription_Found_ReturnsOk()
    {
        _serviceMock.UpdateSubscriptionAsync(1, Arg.Any<UpdateWebhookSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookSubscriptionDto { Id = 1, EventType = "updated" });

        var result = await _sut.UpdateSubscription(1, new UpdateWebhookSubscriptionRequest { EventType = "updated", TargetUrl = "https://example.com/hook" }, TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateSubscription_NotFound_ReturnsNotFound()
    {
        _serviceMock.UpdateSubscriptionAsync(999, Arg.Any<UpdateWebhookSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns((WebhookSubscriptionDto?)null);

        var result = await _sut.UpdateSubscription(999, new UpdateWebhookSubscriptionRequest { EventType = "X", TargetUrl = "https://example.com" }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task DeleteSubscription_Deleted_ReturnsNoContent()
    {
        _serviceMock.DeleteSubscriptionAsync(1, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.DeleteSubscription(1, TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteSubscription_NotFound_ReturnsNotFound()
    {
        _serviceMock.DeleteSubscriptionAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.DeleteSubscription(999, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }
}
