// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// Negative RBAC sweep: a freshly created <c>Reader</c> must be refused write/admin operations
/// through the real <c>ResourceAuthorizationService</c> + controller permission checks. The
/// seeded Reader role grants global <c>Read</c> on every resource type (so reads succeed - that
/// is the positive control here) but no <c>Write</c>/<c>Admin</c>, so every mutation must come
/// back <c>403 Forbidden</c>. Without this, a regression that let a Reader mutate admin-gated
/// resources would sail through the admin-only sweeps unnoticed - the security core (org-scoped
/// RBAC) would be wired but untested for denial.
/// </summary>
[Collection(ApiSmokeCollection.Name)]
public sealed class ApiRbacNegativeSmokeTests(ApiSmokeFixture fixture)
{
    [Fact]
    public async Task Reader_CanRead_PositiveControl()
    {
        using var reader = await fixture.CreateReaderClientAsync();

        // Establishes the contrast for the denial tests: the 403s below are about the *operation*
        // (write/admin), not a blanket lockout - the same Reader reads collections just fine.
        using var resp = await reader.GetAsync("/api/projects", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Reader_CreateProject_Returns403()
    {
        using var reader = await fixture.CreateReaderClientAsync();

        using var resp = await reader.PostAsJsonAsync("/api/projects", new CreateProjectRequest
        {
            Name = $"reader-denied-{Guid.NewGuid():N}",
            Description = "must be forbidden"
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Reader_CreateVault_Returns403()
    {
        using var reader = await fixture.CreateReaderClientAsync();

        using var resp = await reader.PostAsJsonAsync("/api/vaults", new CreateVaultRequest
        {
            Name = $"reader-vault-{Guid.NewGuid():N}",
            Description = "must be forbidden",
            ProjectId = fixture.ProjectId
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Reader_DeleteVault_Returns403_BeforeExistenceCheck()
    {
        using var reader = await fixture.CreateReaderClientAsync();

        // The Admin-permission check runs before the existence lookup, so even a non-existent id
        // is refused with 403 (not 404) - proving authorization gates ahead of resource binding.
        using var resp = await reader.DeleteAsync("/api/vaults/999999", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
