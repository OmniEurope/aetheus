// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aetheus.WebAnalytics.Tests;

public sealed class AetheusWebAnalyticsEndpointTests
{
    [Fact]
    public async Task Collect_EnforcesPrivacyValidationAndRequestSize()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        using var gpcRequest = EventRequest(ValidEvent());
        gpcRequest.Headers.Add("Sec-GPC", "1");
        using var gpcResponse = await client.SendAsync(
            gpcRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, gpcResponse.StatusCode);

        using var invalidSchemaResponse = await client.PostAsJsonAsync(
            "/aetheus-analytics/v1/events",
            ValidEvent() with { SchemaVersion = 2 },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSchemaResponse.StatusCode);

        using var unsupportedResponse = await client.PostAsJsonAsync(
            "/aetheus-analytics/v1/events",
            ValidEvent() with { Kind = "session_start" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsupportedResponse.StatusCode);

        using var unsafeRouteResponse = await client.PostAsJsonAsync(
            "/aetheus-analytics/v1/events",
            ValidEvent() with { Route = "/projects/42?secret=value" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unsafeRouteResponse.StatusCode);

        using var acceptedResponse = await client.PostAsJsonAsync(
            "/aetheus-analytics/v1/events",
            ValidEvent(),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, acceptedResponse.StatusCode);

        using var oversizedResponse = await client.PostAsync(
            "/aetheus-analytics/v1/events",
            new StringContent(
                "{\"padding\":\"" + new string('a', 5000) + "\"}",
                System.Text.Encoding.UTF8,
                "application/json"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
    }

    [Fact]
    public async Task Collect_ReturnsTooManyRequestsAfterAddressBudget()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        for (var index = 0; index < 60; index++)
        {
            using var response = await client.PostAsJsonAsync(
                "/aetheus-analytics/v1/events",
                ValidEvent() with { EventId = Guid.NewGuid() },
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync(
            "/aetheus-analytics/v1/events",
            ValidEvent() with { EventId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task PreferenceMutations_RequireSameOriginAndAuthenticatedWriter()
    {
        await using var app = await StartAsync(authenticated: true);
        using var client = app.GetTestClient();

        using var crossOriginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/aetheus-analytics/v1/opt-out");
        crossOriginRequest.Headers.Add("Origin", "https://attacker.invalid");
        using var crossOriginResponse = await client.SendAsync(
            crossOriginRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, crossOriginResponse.StatusCode);

        using var sameOriginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/aetheus-analytics/v1/opt-out");
        sameOriginRequest.Headers.Add("Origin", "http://localhost");
        using var sameOriginResponse = await client.SendAsync(
            sameOriginRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotImplemented, sameOriginResponse.StatusCode);
    }

    private static async Task<WebApplication> StartAsync(bool authenticated = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddAetheusWebAnalytics(
            new ConfigurationBuilder().Build(),
            options =>
            {
                options.Enabled = true;
                options.ApplicationId = 42;
                options.SiteId = "portfolio";
                options.IngestEndpoint = new Uri(
                    "https://aetheus.example/api/ingest/web-analytics/v1/events");
                options.IngestKey = "ingest";
                options.PseudonymizationKey = new string('0', 32);
                options.ExportIntervalMilliseconds = 10_000;
            });

        var app = builder.Build();
        if (authenticated)
        {
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "user-42")],
                        "test"));
                await next(context);
            });
        }
        app.MapAetheusWebAnalytics();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static HttpRequestMessage EventRequest(AnalyticsBrowserEvent analyticsEvent) =>
        new(HttpMethod.Post, "/aetheus-analytics/v1/events")
        {
            Content = JsonContent.Create(analyticsEvent)
        };

    private static AnalyticsBrowserEvent ValidEvent() => new()
    {
        SchemaVersion = 1,
        EventId = Guid.NewGuid(),
        OccurredAtUtc = DateTimeOffset.UtcNow,
        Kind = "page_view",
        Route = "/projects/42"
    };
}
