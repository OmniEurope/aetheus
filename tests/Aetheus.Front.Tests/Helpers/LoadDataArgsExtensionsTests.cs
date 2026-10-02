// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Helpers;

public class LoadDataArgsExtensionsTests
{
    [Fact]
    public void GetPage_NoSkipNoTop_ReturnsPage1()
    {
        var args = new GridLoadArgs();
        Assert.Equal(1, args.GetPage());
    }

    [Fact]
    public void GetPage_WithSkipAndTop_CalculatesCorrectly()
    {
        var args = new GridLoadArgs { Skip = 50, Top = 25 };
        Assert.Equal(3, args.GetPage());
    }

    [Fact]
    public void GetPage_Skip0Top10_ReturnsPage1()
    {
        var args = new GridLoadArgs { Skip = 0, Top = 10 };
        Assert.Equal(1, args.GetPage());
    }

    [Fact]
    public void GetPage_TopZero_FallsBackToDefault()
    {
        var args = new GridLoadArgs { Skip = 0, Top = 0 };
        Assert.Equal(1, args.GetPage());
    }

    [Fact]
    public void GetPageSize_NoTop_ReturnsDefault()
    {
        var args = new GridLoadArgs();
        Assert.Equal(GridLoadArgsExtensions.DefaultPageSize, args.GetPageSize());
    }

    [Fact]
    public void GetPageSize_WithTop_ReturnsTop()
    {
        var args = new GridLoadArgs { Top = 50 };
        Assert.Equal(50, args.GetPageSize());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(500, 200)]
    public void GetPageSize_OutOfRange_IsClamped(int requested, int expected)
    {
        var args = new GridLoadArgs { Top = requested };
        Assert.Equal(expected, args.GetPageSize());
    }

    [Fact]
    public void GetPageSize_CustomDefault()
    {
        var args = new GridLoadArgs();
        Assert.Equal(10, args.GetPageSize(10));
    }

    [Fact]
    public void ToPageRequest_Combined()
    {
        var args = new GridLoadArgs { Skip = 20, Top = 10 };
        var (page, pageSize) = args.ToPageRequest();
        Assert.Equal(3, page);
        Assert.Equal(10, pageSize);
    }

    [Fact]
    public void ToPageRequest_Defaults()
    {
        var args = new GridLoadArgs();
        var (page, pageSize) = args.ToPageRequest();
        Assert.Equal(1, page);
        Assert.Equal(GridLoadArgsExtensions.DefaultPageSize, pageSize);
    }

    [Fact]
    public void DefaultPageSize_Is25()
    {
        Assert.Equal(25, GridLoadArgsExtensions.DefaultPageSize);
    }

    private sealed record Row(string Name, int Size);

    private static readonly List<Row> Rows =
    [
        new("beta", 2),
        new("Alpha", 30),
        new("gamma", 1)
    ];

    /// <summary>The whole collection is in memory, so a sort has to order all of it, not the slice the
    /// pager happens to be showing.</summary>
    [Fact]
    public void ToClientPage_SortsTheWholeCollectionBeforeCuttingThePage()
    {
        var args = new GridLoadArgs { Skip = 0, Top = 2, OrderBy = "Size desc" };

        var page = args.ToClientPage(Rows);

        Assert.Equal([30, 2], page.Select(r => r.Size));
    }

    [Fact]
    public void ToClientPage_FiltersCaseInsensitively_AndCountsWhatIsReachable()
    {
        var args = new GridLoadArgs
        {
            Skip = 0,
            Top = 25,
            Filters = [new GridFilterDescriptor("Name", "A", OmniDataGridFilterOperator.Contains)]
        };

        var page = args.ToClientPage(Rows);

        Assert.Equal(["beta", "Alpha", "gamma"], page.Select(r => r.Name));
        Assert.Equal(3, args.ClientFilteredCount(Rows));
    }

    [Fact]
    public void ToClientPage_NarrowsToTheMatchingRows()
    {
        var args = new GridLoadArgs
        {
            Skip = 0,
            Top = 25,
            Filters = [new GridFilterDescriptor("Name", "mm", OmniDataGridFilterOperator.Contains)]
        };

        Assert.Equal("gamma", Assert.Single(args.ToClientPage(Rows)).Name);
        Assert.Equal(1, args.ClientFilteredCount(Rows));
    }

    /// <summary>A filter or sort naming a property the row type does not have must narrow nothing.
    /// Returning an empty page instead would blank a table over a column mismatch.</summary>
    [Fact]
    public void ToClientPage_IgnoresAnUnknownProperty()
    {
        var args = new GridLoadArgs
        {
            Skip = 0,
            Top = 25,
            OrderBy = "Nope desc",
            Filters = [new GridFilterDescriptor("Nope", "x", OmniDataGridFilterOperator.Contains)]
        };

        Assert.Equal(3, args.ToClientPage(Rows).Count);
        Assert.Equal(3, args.ClientFilteredCount(Rows));
    }

    /// <summary>The ID column's number filter, as the findings routes read it: inclusive bounds and one
    /// identifier left out; a second condition narrows the first; a non-number narrows nothing.</summary>
    [Theory]
    [InlineData(OmniDataGridFilterOperator.Equals, "7", null, null, 7, 7, null)]
    [InlineData(OmniDataGridFilterOperator.NotEquals, "7", null, null, null, null, 7)]
    [InlineData(OmniDataGridFilterOperator.GreaterThan, "7", null, null, 8, null, null)]
    [InlineData(OmniDataGridFilterOperator.GreaterThanOrEquals, "7", null, null, 7, null, null)]
    [InlineData(OmniDataGridFilterOperator.LessThan, "7", null, null, null, 6, null)]
    [InlineData(OmniDataGridFilterOperator.LessThanOrEquals, "7", null, null, null, 7, null)]
    [InlineData(OmniDataGridFilterOperator.GreaterThan, "4.5", null, null, 5, null, null)]
    [InlineData(OmniDataGridFilterOperator.Equals, "4.5", null, null, 5, 4, null)]
    [InlineData(OmniDataGridFilterOperator.GreaterThanOrEquals, "10", OmniDataGridFilterOperator.LessThan, "20", 10, 19, null)]
    [InlineData(OmniDataGridFilterOperator.Equals, "abc", null, null, null, null, null)]
    public void ColumnWholeNumberRange_TurnsTheNumberFilterIntoBounds(
        OmniDataGridFilterOperator comparison, string value, OmniDataGridFilterOperator? second, string? secondValue,
        int? from, int? to, int? not)
    {
        var args = new GridLoadArgs { Filters = [new GridFilterDescriptor("Id", value, comparison, second, secondValue)] };

        Assert.Equal((from, to, not), args.ColumnWholeNumberRange("Id"));
        Assert.Equal(((int?)null, (int?)null, (int?)null), new GridLoadArgs().ColumnWholeNumberRange("Id"));
    }
}
