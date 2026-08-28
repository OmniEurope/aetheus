// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Tests;

public sealed class GitRepositoriesPaginationIntegrationTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task GetRepositories_CrossProjectSecondPage_ReturnsStableSliceAndGlobalTotal()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokenFactory.AdminToken());
        var prefix = $"cross-page-{Guid.NewGuid():N}";
        var (firstProjectId, secondProjectId) = await SeedRepositoriesAsync(factory, prefix);

        using var response = await client.GetAsync(
            $"/api/git/repos?search={Uri.EscapeDataString(prefix)}&page=2&pageSize=2"
            + "&sortBy=Name&sortDescending=false",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PaginatedResult<GitLightRepoDto>>(
            TestJsonOptions.Default,
            TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(
            new[] { $"{prefix}-03", $"{prefix}-04" },
            result.Items.Select(repository => repository.Name).ToArray());
        Assert.Equal(
            new[] { firstProjectId, secondProjectId },
            result.Items.Select(repository => repository.ProjectId).ToArray());
    }

    private static async Task<(int FirstProjectId, int SecondProjectId)> SeedRepositoriesAsync(
        CustomWebApplicationFactory factory,
        string prefix)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organization = await db.Organizations
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);
        if (organization is null)
        {
            organization = new Organization
            {
                Name = $"{prefix}-organization",
                Slug = $"{prefix}-organization"
            };
            db.Organizations.Add(organization);
        }

        var firstProject = new Project
        {
            Name = $"{prefix}-project-a",
            Organization = organization,
            Status = ProjectStatus.Active
        };
        var secondProject = new Project
        {
            Name = $"{prefix}-project-b",
            Organization = organization,
            Status = ProjectStatus.Active
        };
        db.Projects.AddRange(firstProject, secondProject);
        db.GitInternalRepos.AddRange(
            CreateRepository(firstProject, $"{prefix}-01"),
            CreateRepository(firstProject, $"{prefix}-02"),
            CreateRepository(firstProject, $"{prefix}-03"),
            CreateRepository(secondProject, $"{prefix}-04"),
            CreateRepository(secondProject, $"{prefix}-05"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (firstProject.Id, secondProject.Id);
    }

    private static GitInternalRepo CreateRepository(Project project, string name) =>
        new()
        {
            Project = project,
            Name = name,
            Slug = name,
            DefaultBranch = "main",
            IsEmpty = true
        };
}
