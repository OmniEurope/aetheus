// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Auth;

internal static class LoginAuditRecorder
{
    public static async Task RecordFailureAsync(
        IAuditService audit, IHttpContextAccessor accessor, string reason,
        User? user, string? attemptedUsername, CancellationToken ct)
    {
        var (ip, userAgent) = GetClientContext(accessor);
        await audit.LogAsync(
            $"LoginFailed.{reason}", user is null ? "Authentication" : "User", user?.Id,
            $"Username: {Sanitize(attemptedUsername, 100)}, IP: {ip}, UA: {userAgent}", ct)
            .ConfigureAwait(false);
    }

    public static (string Ip, string UserAgent) GetClientContext(IHttpContextAccessor accessor)
    {
        var context = accessor.HttpContext;
        return (context?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            Sanitize(context?.Request.Headers.UserAgent.ToString(), 200));
    }

    public static string Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }
}
