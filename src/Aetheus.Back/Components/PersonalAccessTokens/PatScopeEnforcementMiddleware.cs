// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Http;

namespace Aetheus.Back.Components.PersonalAccessTokens;

/// <summary>
/// Pure decision for read-only PAT enforcement (unit-tested without a pipeline).
/// </summary>
public static class PatScopeEnforcement
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    /// <summary>True when a read-only PAT is attempting a mutating operation. Git Smart HTTP uses
    /// POST for both reads (<c>git-upload-pack</c>) and writes (<c>git-receive-pack</c>), so the
    /// upload endpoint is the sole POST exception.</summary>
    public static bool IsWriteBlocked(bool isReadOnlyPat, string method, PathString path = default)
        => isReadOnlyPat
            && !SafeMethods.Contains(method)
            && !(HttpMethods.IsPost(method)
                && path.Value?.EndsWith("/git-upload-pack", StringComparison.Ordinal) == true);
}

/// <summary>
/// Enforces the read-only scope of a Personal Access Token (PLAN-006 4.5). Runs after authorization,
/// so <c>HttpContext.User</c> already carries the PAT principal on every <c>[Authorize]</c> endpoint.
/// If the principal was minted from a read-only PAT and the request is mutating, it is refused 403
/// before the action executes - so a read-only token can never write, regardless of the user's RBAC.
/// </summary>
internal sealed class PatScopeEnforcementMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var scope = context.User.FindFirst(PatConstants.ScopeClaimType)?.Value;
        var isReadOnly = string.Equals(scope, PatConstants.ReadOnlyScopeValue, StringComparison.Ordinal);

        if (PatScopeEnforcement.IsWriteBlocked(isReadOnly, context.Request.Method, context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { error = "This personal access token is read-only; write operations are not permitted." },
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
