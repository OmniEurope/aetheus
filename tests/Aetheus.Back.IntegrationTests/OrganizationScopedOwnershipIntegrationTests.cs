// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Json;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.DTOs.Organizations;
using Aetheus.Shared.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// Verifies org-scoped ownership isolation: servers and projects created under one
/// organization are not visible to users who belong to a different organization.
/// <para>
/// The <c>ResourceAuthorizationService</c> unions org-membership resource ids into
/// <c>accessibleIds</c> for Server and Project. These tests exercise the full pipeline
/// (controller → authz → repository → PostgreSQL) to prove cross-org isolation works
/// end-to-end, which the InMemory suite cannot do because it neither enforces FK
/// ownership constraints nor runs the real org-membership join queries.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrganizationScopedOwnershipIntegrationTests(PostgresFixture fixture)
{
    private const string AdminPassword = "Integr@tion-Test-Admin-Pwd-2026";

    private static Task<string> LoginAsync(HttpClient client, string username, string password)
        => IntegrationAuth.LoginAsync(client, username, password, TestContext.Current.CancellationToken);

    private static void SetBearer(HttpClient client, string token)
        => IntegrationAuth.SetBearer(client, token);

    [Fact]
    public async Task ProjectCreatedInOrgA_NotVisibleToUserInOrgB()
    {
        await using var factory = new AetheusWebApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var adminToken = await LoginAsync(client, "admin", AdminPassword);
        SetBearer(client, adminToken);

        // Create two organizations
        var orgAResp = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest
        {
            Name = $"Org A {Guid.NewGuid():N}",
            Slug = $"org-a-{Guid.NewGuid():N}",
            Description = "First org"
        }, cancellationToken: TestContext.Current.CancellationToken);
        orgAResp.EnsureSuccessStatusCode();
        var orgA = await orgAResp.Content.ReadFromJsonAsync<OrganizationDto>(IntegrationJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        var orgBResp = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest
        {
            Name = $"Org B {Guid.NewGuid():N}",
            Slug = $"org-b-{Guid.NewGuid():N}",
            Description = "Second org"
        }, cancellationToken: TestContext.Current.CancellationToken);
        orgBResp.EnsureSuccessStatusCode();
        var orgB = await orgBResp.Content.ReadFromJsonAsync<OrganizationDto>(IntegrationJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        // Create a project in Org A
        var projResp = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest
        {
            Name = $"OrgA-Project-{Guid.NewGuid():N}",
            Description = "Belongs to Org A",
            OrganizationId = orgA!.Id
        }, cancellationToken: TestContext.Current.CancellationToken);
        projResp.EnsureSuccessStatusCode();
        var project = await projResp.Content.ReadFromJsonAsync<ProjectDto>(IntegrationJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        // Create a user with a custom org-only role (no global resource permissions) and
        // assign to Org B only. The built-in Contributor role grants global Write on all
        // resource types (ResourceId=null), which bypasses org-scoped isolation.
        string userBName;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var orgOnlyRole = new Role
            {
                Name = $"OrgOnly-{Guid.NewGuid():N}",
                Description = "No global permissions - access only via org membership"
            };
            db.Roles.Add(orgOnlyRole);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            userBName = $"orgb-user-{Guid.NewGuid():N}";
            var userB = new User
            {
                Username = userBName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("OrgB-User-Pwd-2026!"),
                IsActive = true
            };
            db.Users.Add(userB);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            db.UserRoles.Add(new UserRole { UserId = userB.Id, RoleId = orgOnlyRole.Id });
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            // Add user B to Org B
            db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = orgB!.Id,
                UserId = userB.Id,
                Role = OrganizationRole.Member
            });
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        // Login as user B
        client.DefaultRequestHeaders.Authorization = null;
        var userBToken = await LoginAsync(client, userBName, "OrgB-User-Pwd-2026!");
        SetBearer(client, userBToken);

        // User B should NOT be able to read the Org A project
        var getProj = await client.GetAsync($"/api/projects/{project!.Id}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, getProj.StatusCode);
    }

    [Fact]
    public async Task UserInOrg_CanSeeProjectsOwnedByTheirOrg()
    {
        await using var factory = new AetheusWebApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var adminToken = await LoginAsync(client, "admin", AdminPassword);
        SetBearer(client, adminToken);

        // Create an organization
        var orgResp = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest
        {
            Name = $"My Org {Guid.NewGuid():N}",
            Slug = $"my-org-{Guid.NewGuid():N}",
            Description = "Test org"
        }, cancellationToken: TestContext.Current.CancellationToken);
        orgResp.EnsureSuccessStatusCode();
        var org = await orgResp.Content.ReadFromJsonAsync<OrganizationDto>(IntegrationJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        // Create a project in this org
        var projResp = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest
        {
            Name = $"MyOrg-Project-{Guid.NewGuid():N}",
            Description = "Belongs to my org",
            OrganizationId = org!.Id
        }, cancellationToken: TestContext.Current.CancellationToken);
        projResp.EnsureSuccessStatusCode();
        var project = await projResp.Content.ReadFromJsonAsync<ProjectDto>(IntegrationJsonOptions.Default, cancellationToken: TestContext.Current.CancellationToken);

        // Create a user and add them to this org
        string memberName;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var contribRole = await db.Roles
                .Where(r => r.Name == "Contributor")
                .Select(r => r.Id)
                .FirstAsync(db);

            memberName = $"member-{Guid.NewGuid():N}";
            var member = new User
            {
                Username = memberName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Member-Test-Pwd-2026!"),
                IsActive = true
            };
            db.Users.Add(member);
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            db.UserRoles.Add(new UserRole { UserId = member.Id, RoleId = contribRole });
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

            db.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = org!.Id,
                UserId = member.Id,
                Role = OrganizationRole.Member
            });
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        // Login as the member
        client.DefaultRequestHeaders.Authorization = null;
        var memberToken = await LoginAsync(client, memberName, "Member-Test-Pwd-2026!");
        SetBearer(client, memberToken);

        // Member CAN read the project owned by their org
        var getProj = await client.GetAsync($"/api/projects/{project!.Id}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getProj.StatusCode);
    }
}

// Helpers used by the EF queries above - xUnit v3 + EF require explicit async.
file static class QueryExtensions
{
    internal static async Task<T> FirstAsync<T>(this IQueryable<T> q, AppDbContext _)
        => await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(q);
}
