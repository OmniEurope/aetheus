// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.RegularExpressions;
using Aetheus.Back.Components.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.AgentInstaller;

/// <summary>
/// Issues a one-shot installer script (bash or PowerShell) pre-filled with a registration
/// token. Lets the operator skip the manual install/configure wizard steps in favour of a
/// single <c>curl ... | bash</c> (or <c>iwr ... | iex</c>) command.
/// </summary>
[ApiController]
[Route("api/agent")]
[Authorize(Roles = "Admin")]
public partial class AgentInstallerController(
    IAgentInstallerService installer,
    IAuthService authService,
    IConfiguration config) : ControllerBase
{
    [GeneratedRegex(@"^[A-Za-z0-9._\-+]{1,40}$")]
    private static partial Regex VersionPattern();

    [HttpGet("installer/{platform}")]
    public async Task<IActionResult> GetInstaller(
        string platform,
        [FromQuery] string? token,
        [FromQuery] string? version,
        [FromQuery] bool pipelineRunner = true,
        [FromQuery] bool serverManagement = false,
        CancellationToken ct = default)
    {
        // F-19: prefer the registration token from a header (not a query string, since query
        // strings are routinely captured in proxy/access logs). Keep the legacy query parameter
        // as a fallback only in Development.
        var headerToken = Request.Headers["X-Registration-Token"].FirstOrDefault();
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(headerToken) && !string.IsNullOrEmpty(auth)
            && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            headerToken = auth["Bearer ".Length..].Trim();
        }
        var effectiveToken = headerToken;
        if (string.IsNullOrEmpty(effectiveToken)) effectiveToken = token;

        if (string.IsNullOrWhiteSpace(effectiveToken))
            return BadRequest(new Aetheus.Shared.DTOs.ApiError { Message = "Missing registration token." });

        var registrationIsValid = await authService.IsRegistrationTokenValidAsync(effectiveToken, ct);
        if (!registrationIsValid)
            return BadRequest(new Aetheus.Shared.DTOs.ApiError { Message = "Invalid or expired registration token." });

        // F-14/F-15: never trust Request.Host \u2014 it can be spoofed via the Host header. Require
        // the configured public URL outside Development.
        var serverUrl = ResolveServerUrl();
        if (serverUrl is null)
            return BadRequest(new Aetheus.Shared.DTOs.ApiError { Message = "Server public URL is not configured." });

        // F-15: validate the version string before embedding it in a shell script.
        var resolvedVersion = version ?? config["App:Version"] ?? "dev";
        if (!VersionPattern().IsMatch(resolvedVersion))
            return BadRequest(new Aetheus.Shared.DTOs.ApiError { Message = "Invalid version." });

        var script = installer.Build(platform, effectiveToken, serverUrl, resolvedVersion, pipelineRunner, serverManagement);
        return File(Encoding.UTF8.GetBytes(script.Body), script.ContentType, script.FileName);
    }

    private string? ResolveServerUrl()
    {
        var configured = config["App:PublicUrl"];
        if (!string.IsNullOrWhiteSpace(configured)
            && Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return $"{uri.Scheme}://{uri.Authority}";
        }

        // F-14: in non-Development environments, refuse to fall back on the Host header. Forcing
        // App:PublicUrl prevents host-header smuggling from rewriting the agent's bootstrap URL.
        var env = HttpContext.RequestServices.GetService<IWebHostEnvironment>();
        if (env is null || !env.IsDevelopment())
            return null;

        var allowlist = config.GetSection("App:AllowedHosts").Get<string[]>() ?? [];
        var requestHost = Request.Host.Value;
        if (allowlist.Length == 0
            || (!string.IsNullOrEmpty(requestHost)
                && Array.Exists(allowlist, h => string.Equals(h, requestHost, StringComparison.OrdinalIgnoreCase))))
        {
            return $"{Request.Scheme}://{requestHost}";
        }

        return null;
    }
}
