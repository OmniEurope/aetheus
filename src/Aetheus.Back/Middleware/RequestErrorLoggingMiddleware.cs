// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Middleware;

public sealed class RequestErrorLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestErrorLoggingMiddleware> logger)
{
    public const string CorrelationHeader = "X-Aetheus-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.TraceIdentifier;
        context.Response.Headers[CorrelationHeader] = correlationId;

        await next(context);

        if (context.Response.StatusCode < StatusCodes.Status400BadRequest
            || context.Response.StatusCode == 499)
            return;

        var level = context.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogLevel.Error
            : LogLevel.Warning;
        logger.Log(
            level,
            "HTTP {StatusCode} for {Method} {Path}; correlation {CorrelationId}",
            context.Response.StatusCode,
            context.Request.Method,
            context.Request.Path,
            correlationId);
    }
}
