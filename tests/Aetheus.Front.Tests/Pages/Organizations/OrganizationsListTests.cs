// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.DTOs.Organizations;
using Bunit;
using Radzen;
using OrganizationsPage = Aetheus.Front.Pages.Organizations.Organizations;

namespace Aetheus.Front.Tests.Pages;

public class OrganizationsListTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public OrganizationsListTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);
    }

    [Fact]
    public void Renders_WithData()
    {
        var data = new PaginatedResult<OrganizationDto>
        {
            Items =
            [
                new OrganizationDto(1, "Acme", "acme", "Acme Corp", 5, 3, DateTime.UtcNow, DateTime.UtcNow),
                new OrganizationDto(2, "Beta", "beta", "Beta Inc", 2, 1, DateTime.UtcNow, DateTime.UtcNow)
            ],
            TotalCount = 2
        };
        _handler.SetJsonResponse("api/organizations", data);

        var cut = Render<OrganizationsPage>();
        // After load, markup should contain org names
        cut.WaitForState(() => cut.Markup.Contains("Acme"));
        Assert.Contains("Beta", cut.Markup);
    }

    [Fact]
    public void Renders_EmptyList()
    {
        var data = new PaginatedResult<OrganizationDto> { Items = [], TotalCount = 0 };
        _handler.SetJsonResponse("api/organizations", data);

        var cut = Render<OrganizationsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("ProgressBarCircular"), TimeSpan.FromSeconds(2));
        Assert.Contains("Organizations", cut.Markup);
    }

    [Fact]
    public async Task LoadData_RequestsRequestedServerPageAndSort()
    {
        _handler.SetJsonResponse("api/organizations", new PaginatedResult<OrganizationDto>
        {
            Items = [],
            TotalCount = 250,
            Page = 2,
            PageSize = 25
        });
        var cut = Render<OrganizationsPage>();
        var method = typeof(OrganizationsPage).GetMethod("LoadDataAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance,
            [new LoadDataArgs { Skip = 25, Top = 25, OrderBy = "Slug desc" }])!);

        Assert.Contains(_handler.Requests, request =>
            request.Url.Contains("page=2", StringComparison.Ordinal)
            && request.Url.Contains("pageSize=25", StringComparison.Ordinal)
            && request.Url.Contains("sortBy=Slug", StringComparison.Ordinal)
            && request.Url.Contains("sortDescending=true", StringComparison.Ordinal));
    }
}
