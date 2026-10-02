// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Shared;

/// <summary>
/// PLAN-005 lot 8 / D47: the page asked for survives the sign-in. Every involuntary bounce to the
/// login page carries the current base-relative path (route, query and fragment) as
/// <c>returnUrl</c>; the login page, and the forced password change after it, come back to it.
/// One place builds the parameter and one place validates it, so an open redirect cannot slip in
/// through a second, laxer copy.
/// </summary>
public static class LoginRedirect
{
    public const string LoginPath = "/login";
    public const string ChangePasswordPath = "/account/change-password";
    private const string Parameter = "returnUrl";

    /// <summary>The login URL that brings the user back to where they are now.</summary>
    public static string ToLogin(NavigationManager nav) =>
        WithReturnUrl(LoginPath, Current(nav));

    /// <summary>The forced password change, bringing the user back to where they are now.</summary>
    public static string ToChangePassword(NavigationManager nav) =>
        WithReturnUrl(ChangePasswordPath, Current(nav));

    /// <summary>
    /// <paramref name="path"/> with <paramref name="returnUrl"/> attached, once validated; the bare
    /// path when there is nothing safe worth coming back to.
    /// </summary>
    public static string WithReturnUrl(string path, string? returnUrl) =>
        IsSafe(returnUrl) && !IsAuthPage(returnUrl!)
            ? $"{path}?{Parameter}={Uri.EscapeDataString(returnUrl!)}"
            : path;

    /// <summary>Where to go after signing in: the requested page when it is safe, else the dashboard.</summary>
    public static string Resolve(string? returnUrl) =>
        IsSafe(returnUrl) && !IsAuthPage(returnUrl!) ? returnUrl! : "/";

    /// <summary>
    /// Only a path of this application: it starts with a single <c>/</c>, names no scheme or host
    /// (<c>//evil</c>, <c>/\evil</c>, <c>https://evil</c>), and holds no backslash or control
    /// character a browser could normalize into one.
    /// </summary>
    internal static bool IsSafe(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl.Length > 2048) return false;
        if (returnUrl[0] != '/' || returnUrl.StartsWith("//", StringComparison.Ordinal)) return false;
        if (returnUrl.Contains('\\') || returnUrl.Any(char.IsControl)) return false;
        // A path never needs a scheme; "/redirect?to=https://x" is fine, "/https://x" is not a scheme.
        var pathPart = returnUrl.Split('?', '#')[0];
        return !pathPart.Contains("://", StringComparison.Ordinal);
    }

    private static string Current(NavigationManager nav) => "/" + nav.ToBaseRelativePath(nav.Uri);

    private static bool IsAuthPage(string returnUrl)
    {
        var path = returnUrl.Split('?', '#')[0].TrimEnd('/');
        return path.Length == 0
            || path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase)
            || path.Equals(ChangePasswordPath, StringComparison.OrdinalIgnoreCase);
    }
}
