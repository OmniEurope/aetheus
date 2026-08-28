// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Aetheus.Back.Components.Auth;

/// <summary>Records model-binding/validation failures that short-circuit before AuthService.</summary>
public sealed class LoginValidationAuditFilter(IAuditService audit) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        var executed = await next();
        var statusCode = (executed.Result as IStatusCodeActionResult)?.StatusCode
            ?? context.HttpContext.Response.StatusCode;
        if (statusCode != StatusCodes.Status400BadRequest) return;

        var (ip, userAgent) = LoginAuditRecorder.GetClientContext(
            new HttpContextAccessor { HttpContext = context.HttpContext });
        await audit.LogAsync(
            "LoginFailed.InvalidRequest",
            "Authentication",
            details: $"IP: {ip}, UA: {userAgent}",
            ct: context.HttpContext.RequestAborted).ConfigureAwait(false);
    }
}
