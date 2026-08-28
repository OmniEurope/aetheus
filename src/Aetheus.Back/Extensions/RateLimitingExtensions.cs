// SPDX-License-Identifier: EUPL-1.2
using System.Threading.RateLimiting;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Configuration;
using Microsoft.AspNetCore.RateLimiting;

namespace Aetheus.Back.Extensions;

internal static class RateLimitingExtensions
{
    /// <summary>
    /// Registers the named ingress policies and the authenticated global ceiling.
    /// Integration fixtures can disable the named policies without changing production defaults.
    /// </summary>
    public static IServiceCollection AddAetheusRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.HttpContext.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    var audit = context.HttpContext.RequestServices.GetRequiredService<IAuditService>();
                    var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var userAgent = context.HttpContext.Request.Headers.UserAgent.ToString();
                    var cleanedUserAgent = new string(userAgent.Where(c => !char.IsControl(c)).Take(200).ToArray());
                    await audit.LogAsync("LoginFailed.RateLimited", "Authentication", details:
                        $"IP: {ip}, UA: {(string.IsNullOrEmpty(cleanedUserAgent) ? "unknown" : cleanedUserAgent)}", ct: ct);
                }

                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiError { Message = "Too many authentication attempts. Try again shortly." }, ct);
            };
            if (configuration.GetValue("RateLimiting:Disabled", false))
            {
                AddDisabledPolicies(options);
                return;
            }

            options.AddPolicy("login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => FixedWindow(20)));
            options.AddPolicy("enrollment", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => FixedWindow(5)));
            options.AddPolicy("auth-token", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.Identity?.IsAuthenticated == true
                        ? $"user:{context.User.Identity.Name}"
                        : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                    _ => FixedWindow(60)));
            options.AddPolicy("webhook", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => FixedWindow(30)));

            // Fail closed before ingest-key authentication: an untrusted caller can rotate arbitrary
            // header values, so a key-derived bucket would grant a fresh allowance on every request.
            // A validated app identity is not available at middleware time; partition by address here
            // and let the ingest authentication/cache enforce the per-app boundary afterwards.
            options.AddPolicy("otlp-ingest", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveOtlpPartitionKey(context),
                    _ => FixedWindow(120)));
            options.AddPolicy("web-analytics-public", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    "analytics-address:"
                    + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    _ => FixedWindow(60)));
            options.AddPolicy("external-login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    "external-login:"
                    + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    _ => FixedWindow(5)));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                if (path.EndsWith("/api/auth/external-login", StringComparison.OrdinalIgnoreCase))
                    return RateLimitPartition.GetFixedWindowLimiter(
                        "external-login-global",
                        _ => FixedWindow(60));

                var (partitionKey, permitLimit) = ResolveGlobalLimit(context);
                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = BackendRuntimeDefaults.RateLimitWindow,
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    });
            });
        });

        return services;
    }

    internal static (string PartitionKey, int PermitLimit) ResolveGlobalLimit(HttpContext context)
    {
        if (string.Equals(
                context.User.Identity?.AuthenticationType,
                AgentTokenAuthenticationHandler.SchemeName,
                StringComparison.Ordinal))
        {
            var serverId = context.User.FindFirst("ServerId")?.Value ?? "unknown";
            return ($"agent:{serverId}", BackendRuntimeDefaults.AuthenticatedAgentRateLimit);
        }

        return context.User.Identity?.IsAuthenticated == true
            ? ($"u:{context.User.Identity.Name}", BackendRuntimeDefaults.AuthenticatedUserRateLimit)
            : ($"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                BackendRuntimeDefaults.AuthenticatedUserRateLimit);
    }

    internal static string ResolveOtlpPartitionKey(HttpContext context) =>
        $"otlp-address:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    private static void AddDisabledPolicies(RateLimiterOptions options)
    {
        foreach (var name in new[]
                 {
                     "login", "enrollment", "auth-token", "webhook", "external-login", "otlp-ingest", "web-analytics-public"
                 })
            options.AddPolicy(name, _ => RateLimitPartition.GetNoLimiter("all"));
    }

    private static FixedWindowRateLimiterOptions FixedWindow(int permitLimit) =>
        new()
        {
            PermitLimit = permitLimit,
            Window = BackendRuntimeDefaults.RateLimitWindow,
            QueueLimit = 0
        };
}
