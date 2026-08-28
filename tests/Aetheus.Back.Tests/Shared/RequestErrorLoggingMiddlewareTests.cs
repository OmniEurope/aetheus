// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Aetheus.Back.Tests;

public sealed class RequestErrorLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ErrorResponse_AddsCorrelationHeaderAndLogs()
    {
        var logger = Substitute.For<ILogger<RequestErrorLoggingMiddleware>>();
        var middleware = new RequestErrorLoggingMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }, logger);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "request-42";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("request-42", context.Response.Headers[RequestErrorLoggingMiddleware.CorrelationHeader]);
        logger.ReceivedWithAnyArgs(1).Log(
            default,
            default,
            default!,
            null,
            default!);
    }
}
