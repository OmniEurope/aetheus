// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;

namespace Aetheus.Back.Tests.Auth;

public sealed class JwtRevocationClaimsIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task SignedJwtWithoutSubjectAndSecurityStamp_IsRejected()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestTokenFactory.GenerateToken(includeRevocationClaims: false));

        var response = await client.GetAsync("/api/monitoring/dashboard", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
