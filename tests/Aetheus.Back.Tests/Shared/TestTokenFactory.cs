// SPDX-License-Identifier: EUPL-1.2
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Aetheus.Back.Components.Auth;
using Microsoft.IdentityModel.Tokens;

namespace Aetheus.Back.Tests;

/// <summary>
/// Centralised JWT token generator for integration and unit tests.
/// Eliminates duplicated token-creation logic across test classes.
/// </summary>
public static class TestTokenFactory
{
    public const string DefaultKey = "aetheus-dev-key-minimum-32-bytes!!";
    public const string Issuer = "Aetheus";
    public const string Audience = "Aetheus";

    /// <summary>
    /// Generates a signed JWT token with configurable claims.
    /// </summary>
    public static string GenerateToken(
        string username = "admin",
        string role = "Admin",
        string? key = null,
        TimeSpan? expiration = null,
        IEnumerable<Claim>? additionalClaims = null,
        bool includeRevocationClaims = true)
    {
        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(key ?? DefaultKey));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role)
        };

        var extras = additionalClaims?.ToList() ?? [];
        claims.AddRange(extras);
        if (includeRevocationClaims && !extras.Any(claim => claim.Type == ClaimTypes.NameIdentifier))
            claims.Add(new Claim(ClaimTypes.NameIdentifier, "bootstrap"));
        if (includeRevocationClaims && !extras.Any(claim => claim.Type == AetheusClaimTypes.SecurityStamp))
            claims.Add(new Claim(AetheusClaimTypes.SecurityStamp, "bootstrap"));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(expiration ?? TimeSpan.FromHours(1)),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Admin token - the most common case in integration tests.</summary>
    public static string AdminToken() => GenerateToken();

    /// <summary>Non-admin user token for permission-boundary tests.</summary>
    public static string UserToken(string username = "user") => GenerateToken(username, "User");
}
