// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests.AppMonitoring;

public sealed class PublicWebAnalyticsControllerTests
{
    private readonly IAppWebAnalyticsConfigurationService _configuration =
        Substitute.For<IAppWebAnalyticsConfigurationService>();
    private readonly IAppWebAnalyticsService _analytics =
        Substitute.For<IAppWebAnalyticsService>();

    [Fact]
    public async Task Collect_GlobalPrivacyControlStopsBeforeConfigurationLookup()
    {
        var controller = Controller();
        controller.Request.Headers["Sec-GPC"] = "1";

        var result = await controller.Collect(
            "portfolio",
            Request(),
            TestContext.Current.CancellationToken);

        Assert.IsType<NoContentResult>(result);
        await _configuration.DidNotReceive().ResolvePublicContextAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Collect_DisallowedOriginFailsClosed()
    {
        var controller = Controller();
        controller.Request.Headers.Origin = "https://attacker.example";

        var result = await controller.Collect(
            "portfolio",
            Request(),
            TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
        await _analytics.DidNotReceive().IngestAsync(
            Arg.Any<int>(),
            Arg.Any<IReadOnlyList<AppWebAnalyticsIngestEvent>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Collect_AllowedOriginComputesServerPseudonymsAndReportsReplay()
    {
        var controller = Controller();
        controller.Request.Headers.Origin = "https://portfolio.example";
        _configuration.ResolvePublicContextAsync(
                "portfolio",
                "https://portfolio.example",
                Arg.Any<CancellationToken>())
            .Returns(new PublicAnalyticsContext(new MonitoredApp
            {
                Id = 7,
                AnalyticsSiteId = "portfolio",
                AnalyticsPseudonymKeyVersion = 2
            }, "server-only-secret"));
        _analytics.IngestAsync(
                7,
                Arg.Any<IReadOnlyList<AppWebAnalyticsIngestEvent>>(),
                Arg.Any<CancellationToken>())
            .Returns(new WebAnalyticsIngestOutcome(0, 1, 0));

        var result = await controller.Collect(
            "portfolio",
            Request(),
            TestContext.Current.CancellationToken);

        Assert.IsType<ConflictResult>(result);
        await _analytics.Received(1).IngestAsync(
            7,
            Arg.Is<IReadOnlyList<AppWebAnalyticsIngestEvent>>(events =>
                events.Count == 1
                && events[0].ApplicationId == 7
                && events[0].DailyPseudonym.Length == 64
                && events[0].Route == "/orders/{id}"),
            Arg.Any<CancellationToken>());
        Assert.Equal(
            "https://portfolio.example",
            controller.Response.Headers.AccessControlAllowOrigin);
    }

    private PublicWebAnalyticsController Controller()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.12");
        context.Request.Headers.UserAgent = "UnitBrowser/1.0";
        return new PublicWebAnalyticsController(
            _configuration,
            _analytics,
            new AppAnalyticsSiteRateLimiter())
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static PublicWebAnalyticsEventRequest Request() => new()
    {
        SchemaVersion = 1,
        EventId = Guid.NewGuid(),
        OccurredAtUtc = DateTime.UtcNow,
        Kind = "page_view",
        Route = "/Orders/42"
    };
}
