// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Radzen;

namespace Aetheus.Front.Tests.Pages;

public class VersionHistoryDialogTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public VersionHistoryDialogTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public async Task Renders_EmptyPage()
    {
        _handler.SetJsonResponse("api/variable-libraries/1/entries/2/versions",
            new PaginatedResult<VariableEntryVersionDto>());
        var cut = Render<VersionHistoryDialog>(p => p
            .Add(x => x.LibraryId, 1)
            .Add(x => x.EntryId, 2));
        await LoadAsync(cut);

        // The history grid renders its column headers even with no version rows.
        Assert.Contains("Key", cut.Markup);
        Assert.Contains("Date", cut.Markup);
        Assert.Empty(cut.FindAll(".rz-data-row"));
    }

    [Fact]
    public async Task Renders_CurrentPage_AndUsesServerPagination()
    {
        var versions = new List<VariableEntryVersionDto>
        {
            new() { Version = 1, Key = "ENV", Value = "dev", ChangeType = ChangeType.Created, ChangedAt = new DateTime(2026, 1, 1) },
            new() { Version = 2, Key = "ENV", Value = "prod", ChangeType = ChangeType.Updated, ChangedAt = new DateTime(2026, 2, 1) }
        };
        _handler.SetJsonResponse("api/variable-libraries/1/entries/2/versions",
            new PaginatedResult<VariableEntryVersionDto>
            {
                Items = versions,
                TotalCount = 52,
                Page = 2,
                PageSize = 25
            });
        var cut = Render<VersionHistoryDialog>(p => p
            .Add(x => x.LibraryId, 1)
            .Add(x => x.EntryId, 2));
        await LoadAsync(cut, new LoadDataArgs { Skip = 25, Top = 25, OrderBy = "Version desc" });

        Assert.Contains("ENV", cut.Markup);
        Assert.Contains(_handler.Requests, request => request.Url.Contains("page=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Renders_DeletedVersion()
    {
        var versions = new List<VariableEntryVersionDto>
        {
            new() { Version = 1, Key = "OLD_VAR", Value = "", ChangeType = ChangeType.Deleted, ChangedAt = DateTime.UtcNow }
        };
        _handler.SetJsonResponse("api/variable-libraries/1/entries/2/versions",
            new PaginatedResult<VariableEntryVersionDto> { Items = versions, TotalCount = 1 });
        var cut = Render<VersionHistoryDialog>(p => p
            .Add(x => x.LibraryId, 1)
            .Add(x => x.EntryId, 2));
        await LoadAsync(cut);

        Assert.Contains("OLD_VAR", cut.Markup);
    }

    private static async Task LoadAsync(
        IRenderedComponent<VersionHistoryDialog> cut, LoadDataArgs? args = null)
    {
        var method = typeof(VersionHistoryDialog).GetMethod(
            "LoadDataAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(
            cut.Instance, [args ?? new LoadDataArgs { Skip = 0, Top = 25 }])!);
        cut.Render();
    }
}
