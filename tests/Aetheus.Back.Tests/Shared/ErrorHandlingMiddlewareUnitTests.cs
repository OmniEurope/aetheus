// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Exceptions;
using Aetheus.Back.Middleware;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ErrorHandlingMiddlewareUnitTests
{
    private readonly ILogger<ErrorHandlingMiddleware> _logger = Substitute.For<ILogger<ErrorHandlingMiddleware>>();

    [Fact]
    public async Task InvokeAsync_NoException_CallsNext()
    {
        var called = false;
        var middleware = new ErrorHandlingMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, _logger);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);
        Assert.True(called);
    }

    [Fact]
    public async Task InvokeAsync_NotFoundException_Returns404()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new NotFoundException("not found"),
            _logger);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        var error = await ReadBody(context);
        Assert.Equal("not found", error!.Message);
        Assert.Equal(context.TraceIdentifier, error.CorrelationId);
    }

    [Fact]
    public async Task InvokeAsync_BadRequestException_Returns400()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new BadRequestException("bad"),
            _logger);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);

        Assert.Equal(400, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_ConflictException_Returns409()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new ConflictException("conflict"),
            _logger);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);

        Assert.Equal(409, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceled_Returns499()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var middleware = new ErrorHandlingMiddleware(
            _ => throw new OperationCanceledException(cts.Token),
            _logger);

        var context = new DefaultHttpContext();
        context.RequestAborted = cts.Token;
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_Returns500()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            _logger);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        var error = await ReadBody(context);
        Assert.Contains("unexpected", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_OversizedRequest_ReturnsExplicit413()
    {
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new BadHttpRequestException(
                "Request body too large.", StatusCodes.Status413PayloadTooLarge),
            _logger);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        var error = await ReadBody(context);
        Assert.Contains("too large", error!.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ApiError?> ReadBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return JsonSerializer.Deserialize<ApiError>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
