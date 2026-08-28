// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Configuration;
using Microsoft.AspNetCore.RateLimiting;

namespace Aetheus.Back.Components.Auth;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public class AuthController(
    IAuthService authService,
    IServerEnrollmentService enrollment,
    IConfiguration configuration) : ControllerBase
{
    private const string ExternalGatewayHeader = "X-Aetheus-External-Auth";

    [AllowAnonymous]
    [HttpGet("public-demo")]
    public ActionResult<PublicDemoInfoDto> GetPublicDemoInfo()
        => Ok(new PublicDemoInfoDto { Enabled = PublicDemoConfiguration.IsEnabled(configuration) });

    [AllowAnonymous]
    [EnableRateLimiting("enrollment")]
    [HttpPost("register")]
    public async Task<ActionResult<ServerRegistrationResponse>> RegisterServer(
        [FromBody] ServerRegistrationRequest request, CancellationToken ct)
    {
        var result = await enrollment.RegisterServerAsync(request, ct);
        if (result is null)
            return BadRequest(new ApiError { Message = "Invalid or expired registration token." });
        return Ok(result);
    }

    [AllowAnonymous]
    [ServiceFilter(typeof(LoginValidationAuditFilter))]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        if (result is null)
            return Unauthorized(new ApiError { Message = "Invalid credentials." });
        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("registration-tokens")]
    public async Task<ActionResult<RegistrationTokenDto>> CreateRegistrationToken(
        [FromBody] CreateRegistrationTokenRequest request, CancellationToken ct)
    {
        var token = await authService.CreateRegistrationTokenAsync(request, ct);
        return Ok(token);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("registration-tokens")]
    public async Task<ActionResult<List<RegistrationTokenDto>>> GetRegistrationTokens(CancellationToken ct)
    {
        var tokens = await authService.GetRegistrationTokensAsync(ct);
        return Ok(tokens);
    }

    // RTOK: single-token read so the agent-wizard verify step polls just its own token's IsUsed flag
    // instead of re-downloading the full token list every 3 s.
    [Authorize(Roles = "Admin")]
    [HttpGet("registration-tokens/{id:int}")]
    public async Task<ActionResult<RegistrationTokenDto>> GetRegistrationToken(int id, CancellationToken ct)
    {
        var token = await authService.GetRegistrationTokenAsync(id, ct);
        return token is null ? NotFound() : Ok(token);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("servers/{serverId:int}/rotate-token")]
    public async Task<ActionResult<ServerRegistrationResponse>> RotateAgentToken(int serverId, CancellationToken ct)
    {
        var result = await authService.RotateAgentTokenAsync(serverId, ct);
        if (result is null)
            return NotFound(new ApiError { Message = "Server not found." });
        return Ok(result);
    }

    /// <summary>
    /// Logs in a user authenticated by a trusted external identity provider (OIDC/SSO).
    /// <para>
    /// TOTP exemption (audit 360, by design): external login does NOT enforce the local TOTP second
    /// factor, even for a local user who has TOTP enabled. The second factor is delegated to the
    /// external IdP (which performs its own MFA); re-prompting for the local TOTP here would be
    /// redundant and would break the SSO flow. Local password login (<c>/login</c>) still enforces
    /// TOTP. If the trust assumption on the external IdP ever changes, revisit this exemption.
    /// </para>
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("external-login")]
    [HttpPost("external-login")]
    public async Task<ActionResult<LoginResponse>> ExternalLogin(
        [FromBody] ExternalLoginRequest request, CancellationToken ct)
    {
        // Provider/subject claims arrive from a reverse-proxy/IdP gateway, not from the end
        // user. Enabling this endpoint without authenticating that gateway would let anyone
        // mint an identity by posting an arbitrary SubjectId. Fail closed unless both sides
        // share a dedicated secret; compare hashes in constant time and never log the value.
        var expectedGatewaySecret = configuration["Auth:ExternalLogin:GatewaySecret"];
        if (string.IsNullOrWhiteSpace(expectedGatewaySecret))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiError { Message = "External identity gateway is not configured." });

        var suppliedGatewaySecret = Request.Headers[ExternalGatewayHeader].FirstOrDefault();
        if (!GatewaySecretsMatch(expectedGatewaySecret, suppliedGatewaySecret))
            return Unauthorized(new ApiError { Message = "External identity gateway authentication failed." });

        var result = await authService.HandleExternalLoginAsync(
            request.Provider, request.SubjectId, request.DisplayName, request.Email, ct);
        if (result is null)
            return Unauthorized(new ApiError { Message = "External login failed or account is disabled." });
        return Ok(result);
    }

    private static bool GatewaySecretsMatch(string expected, string? supplied)
    {
        if (string.IsNullOrEmpty(supplied)) return false;
        var expectedHash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(expected));
        var suppliedHash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(supplied));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    /// <summary>
    /// Issues a fresh JWT for the currently authenticated caller. The Front calls this when the
    /// existing token is close to expiring so the user is never logged out mid-session.
    /// Requires a still-valid bearer token - the SecurityStamp claim is re-checked, so revoked
    /// users (password change, role change) cannot renew.
    /// </summary>
    [EnableRateLimiting("auth-token")]
    [HttpPost("renew")]
    public async Task<ActionResult<LoginResponse>> Renew(CancellationToken ct)
    {
        var nameId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(nameId, out var userId))
            return Unauthorized(new ApiError { Message = "Token not eligible for renewal." });

        var result = await authService.RenewTokenAsync(userId, ct);
        if (result is null)
            return Unauthorized(new ApiError { Message = "User no longer exists or is disabled." });
        return Ok(result);
    }

    // --- Refresh Token Rotation (F-012) ---

    [AllowAnonymous]
    [EnableRateLimiting("auth-token")]
    [HttpPost("token/refresh")]
    public async Task<ActionResult<LoginResponse>> RefreshToken(
        [FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await authService.RefreshTokenAsync(request.RefreshToken, ct);
        if (result is null)
            return Unauthorized(new ApiError { Message = "Invalid or expired refresh token." });
        return Ok(result);
    }

    // --- TOTP 2FA (F-010) ---

    [HttpPost("totp/setup")]
    public async Task<ActionResult<TotpSetupResponse>> SetupTotp(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();
        var result = await authService.SetupTotpAsync(userId.Value, ct);
        return Ok(result);
    }

    [HttpPost("totp/verify")]
    public async Task<ActionResult> VerifyTotp([FromBody] TotpVerifyRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();
        var ok = await authService.VerifyAndEnableTotpAsync(userId.Value, request.Code, ct);
        if (!ok) return BadRequest(new ApiError { Message = "Invalid TOTP code." });
        return Ok();
    }

    [HttpPost("totp/disable")]
    public async Task<ActionResult> DisableTotp([FromBody] TotpDisableRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();
        var ok = await authService.DisableTotpAsync(userId.Value, request.Password, ct);
        if (!ok) return BadRequest(new ApiError { Message = "Invalid password or TOTP not enabled." });
        return Ok();
    }

    // --- Lockout (F-011) ---

    [Authorize(Roles = "Admin")]
    [HttpPost("users/{userId:int}/unlock")]
    public async Task<ActionResult> UnlockUser(int userId, CancellationToken ct)
    {
        var ok = await authService.UnlockUserAsync(userId, ct);
        if (!ok) return NotFound(new ApiError { Message = "User not found." });
        return Ok();
    }

    private int? GetUserId()
    {
        var nameId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(nameId, out var userId) ? userId : null;
    }
}
