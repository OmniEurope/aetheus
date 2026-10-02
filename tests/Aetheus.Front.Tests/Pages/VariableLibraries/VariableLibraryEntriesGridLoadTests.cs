// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.VariableLibraries;
using Bunit;

namespace Aetheus.Front.Tests.Pages;

/// <summary>
/// The detail endpoint never populates <c>VariableLibraryDetailDto.Entries</c>: rows come from the
/// paged entries endpoint only. The grid does not fire LoadData while Data is pre-bound to a non-null
/// list, so the page must fetch that first page itself, otherwise the header count (queried
/// separately) reads 2 next to an empty grid.
/// </summary>
public class VariableLibraryEntriesGridLoadTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public VariableLibraryEntriesGridLoadTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void EntriesGrid_RendersRowsFromThePagedEndpoint_WhenTheDetailCarriesNone()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "Aetheus" }],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/variable-libraries/1", new VariableLibraryDetailDto
        {
            Id = 1,
            Name = "aetheus-public",
            ProjectId = 1,
            ProjectName = "Aetheus",
            RowVersion = Guid.NewGuid(),
            EntryCount = 2
        });
        _handler.SetJsonResponse("api/variable-libraries/1/entries", new PaginatedResult<VariableEntryDto>
        {
            Items =
            [
                new VariableEntryDto { Id = 1, Key = "SITE_DOMAIN", Value = "aetheus.example", VersionCount = 1 },
                new VariableEntryDto { Id = 2, Key = "DOCS_DOMAIN", Value = "docs.example", VersionCount = 1 }
            ],
            TotalCount = 2,
            Page = 1,
            PageSize = 25
        });

        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 1));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(_handler.Requests, r =>
                r.Method == "GET" && r.Url.Contains("api/variable-libraries/1/entries", StringComparison.Ordinal));
            Assert.Contains("SITE_DOMAIN", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("DOCS_DOMAIN", cut.Markup, StringComparison.Ordinal);
        });
    }
}
