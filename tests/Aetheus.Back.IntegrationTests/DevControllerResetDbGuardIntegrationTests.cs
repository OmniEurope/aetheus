// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using Aetheus.Back.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// Fail-safe guard (incident 2026-06-26): <c>POST /api/dev/reset-db</c> DROPS the entire database,
/// so it must refuse any database whose name does not end with <c>_e2e</c>. The Testcontainers DB is
/// <c>aetheus_it</c> - a stand-in for the dev/prod <c>aetheus</c> database - so the endpoint must
/// be refused (403) and drop nothing. This proves a mis-wired E2E backend that fell back to the dev
/// connection string can no longer wipe real data, without running any E2E flow.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DevControllerResetDbGuardIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ResetDb_OnNonE2eDatabase_IsRefused_AndDropsNothing()
    {
        await using var factory = new AetheusWebApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        IntegrationAuth.SetBearer(client, await IntegrationAuth.LoginAsAdminAsync(client, TestContext.Current.CancellationToken));

        var resp = await client.PostAsync("/api/dev/reset-db", null, cancellationToken: TestContext.Current.CancellationToken);

        // The container DB is "aetheus_it" (not *_e2e), so the destructive endpoint must refuse.
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        // And it dropped nothing: the seeded admin user (and the schema) are still intact.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Users.AnyAsync(u => u.Username == "admin", cancellationToken: TestContext.Current.CancellationToken));
    }
}
