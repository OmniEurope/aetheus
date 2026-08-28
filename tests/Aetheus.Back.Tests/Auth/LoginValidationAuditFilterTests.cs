// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Aetheus.Back.Tests.Auth;

public sealed class LoginValidationAuditFilterTests
{
    [Fact]
    public async Task OnResourceExecutionAsync_BadRequest_LogsSanitizedClientContext()
    {
        var audit = Substitute.For<IAuditService>();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.42");
        httpContext.Request.Headers.UserAgent = "test-agent\r\ncontrol";
        var (executing, executed) = CreateContexts(httpContext, new BadRequestResult());

        await new LoginValidationAuditFilter(audit).OnResourceExecutionAsync(
            executing, () => Task.FromResult(executed));

        await audit.Received(1).LogAsync(
            "LoginFailed.InvalidRequest",
            "Authentication",
            details: "IP: 192.0.2.42, UA: test-agentcontrol",
            ct: httpContext.RequestAborted);
    }

    [Fact]
    public async Task OnResourceExecutionAsync_NonBadRequest_DoesNotLog()
    {
        var audit = Substitute.For<IAuditService>();
        var httpContext = new DefaultHttpContext();
        var (executing, executed) = CreateContexts(httpContext, new OkResult());

        await new LoginValidationAuditFilter(audit).OnResourceExecutionAsync(
            executing, () => Task.FromResult(executed));

        await audit.DidNotReceiveWithAnyArgs().LogAsync(
            default!, default!, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OnResourceExecutionAsync_ResponseBadRequestWithoutResult_LogsFailure()
    {
        var audit = Substitute.For<IAuditService>();
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        var (executing, executed) = CreateContexts(httpContext, result: null);

        await new LoginValidationAuditFilter(audit).OnResourceExecutionAsync(
            executing, () => Task.FromResult(executed));

        await audit.Received(1).LogAsync(
            "LoginFailed.InvalidRequest",
            "Authentication",
            details: "IP: unknown, UA: unknown",
            ct: httpContext.RequestAborted);
    }

    [Fact]
    public async Task OnResourceExecutionAsync_CancelledRequest_PropagatesRequestTokenToAudit()
    {
        var audit = Substitute.For<IAuditService>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var httpContext = new DefaultHttpContext { RequestAborted = cancellation.Token };
        var (executing, executed) = CreateContexts(httpContext, new BadRequestResult());

        await new LoginValidationAuditFilter(audit).OnResourceExecutionAsync(
            executing, () => Task.FromResult(executed));

        await audit.Received(1).LogAsync(
            "LoginFailed.InvalidRequest",
            "Authentication",
            details: Arg.Any<string>(),
            ct: cancellation.Token);
    }

    private static (ResourceExecutingContext Executing, ResourceExecutedContext Executed) CreateContexts(
        HttpContext httpContext,
        IActionResult? result)
    {
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        var filters = new List<IFilterMetadata>();
        return (
            new ResourceExecutingContext(actionContext, filters, []),
            new ResourceExecutedContext(actionContext, filters) { Result = result });
    }
}
