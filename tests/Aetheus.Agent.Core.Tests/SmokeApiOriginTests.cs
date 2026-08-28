// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// The browser suite authenticates by posting to {E2E_BACKEND_URL}/api/auth/login. The executor used
/// to set that to the smoke step's single `origin`, which is only correct when one host serves both
/// the application and its API. On a deployment that splits them - which is what aetheus-deploy-prod
/// describes, with app.* and aetheus-api.* - the login reached the frontend image, a static file
/// server that answers 405 to a POST, and the whole suite failed in OneTimeSetUp before a single
/// assertion ran.
/// </summary>
public class SmokeApiOriginTests
{
    [Fact]
    public void NoApiOrigin_KeepsTheSuiteOnTheApplicationOrigin()
    {
        var resolved = SmokeOperationExecutor.ResolveApiOrigin(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            "https://app.example.com");

        // Same-origin deployments must be untouched by this change.
        Assert.Equal("https://app.example.com", resolved);
    }

    [Fact]
    public void EmptyApiOrigin_IsTreatedAsUnset()
    {
        var resolved = SmokeOperationExecutor.ResolveApiOrigin(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AETHEUS_SMOKE_API_ORIGIN"] = "   "
            },
            "https://app.example.com");

        // A pipeline that leaves the variable unbound sends whitespace, not absence.
        Assert.Equal("https://app.example.com", resolved);
    }

    [Fact]
    public void DistinctApiOrigin_IsWhereTheSuiteAuthenticates()
    {
        var resolved = SmokeOperationExecutor.ResolveApiOrigin(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AETHEUS_SMOKE_API_ORIGIN"] = "https://api.example.com"
            },
            "https://app.example.com");

        Assert.Equal("https://api.example.com", resolved);
    }

    [Fact]
    public void TrailingSlash_IsTrimmedSoThePathTheSuiteAppendsStaysWellFormed()
    {
        var resolved = SmokeOperationExecutor.ResolveApiOrigin(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AETHEUS_SMOKE_API_ORIGIN"] = "https://api.example.com/"
            },
            "https://app.example.com");

        // The suite composes "{url}/api/auth/login"; a kept slash would make that a double slash.
        Assert.Equal("https://api.example.com", resolved);
    }
}
