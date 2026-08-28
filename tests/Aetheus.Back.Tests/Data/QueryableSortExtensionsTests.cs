// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;

// Deliberately not Aetheus.Back.Tests.Data: that namespace shadows Aetheus.Back.Data for every test
// that resolves entities as Data.Entities.X, which breaks their compilation.
namespace Aetheus.Back.Tests.Querying;

/// <summary>The sort key arrives from a grid header, so it is user input reaching a query. These cover
/// the two things that matter: a valid key really reorders, and anything else lands on the fallback
/// instead of reaching the provider.</summary>
public class QueryableSortExtensionsTests
{
    private sealed record Row(int Id, string Name, DateTime? ClosedAt)
    {
        public List<string> Tags { get; init; } = [];
    }

    private static readonly List<Row> Rows =
    [
        new(2, "beta", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
        new(1, "Alpha", null),
        new(3, "gamma", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
    ];

    private static IQueryable<Row> Query => Rows.AsQueryable();

    [Fact]
    public void OrderByProperty_SortsByTheNamedProperty_InBothDirections()
    {
        Assert.Equal(
            ["Alpha", "beta", "gamma"],
            Query.OrderByProperty("Name", descending: false, r => r.Id).Select(r => r.Name));

        Assert.Equal(
            ["gamma", "beta", "Alpha"],
            Query.OrderByProperty("Name", descending: true, r => r.Id).Select(r => r.Name));
    }

    /// <summary>Grids send the column's declared property name, whose casing does not always match the
    /// entity's.</summary>
    [Fact]
    public void OrderByProperty_MatchesThePropertyNameCaseInsensitively()
    {
        Assert.Equal(
            [1, 2, 3],
            Query.OrderByProperty("id", descending: false, r => r.Name).Select(r => r.Id));
    }

    [Fact]
    public void OrderByProperty_SortsNullablesWithoutThrowing()
    {
        var ordered = Query.OrderByProperty("ClosedAt", descending: false, r => r.Id).ToList();

        Assert.Equal(3, ordered.Count);
        Assert.Null(ordered[0].ClosedAt);
    }

    /// <summary>An unknown key, an empty one, or a collection property must fall back rather than reach
    /// the provider: the name never becomes query text, so the worst case is a missed lookup.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Nope")]
    [InlineData("1; drop table rows")]
    [InlineData("Tags")]
    public void OrderByProperty_FallsBackOnAnythingItCannotResolve(string? sortBy)
    {
        // Descending is requested, yet the fallback keeps its own ascending direction: dropping the key
        // drops the direction with it, so an unrecognised column cannot silently reverse the list.
        var ordered = Query
            .OrderByProperty(sortBy, descending: true, r => r.Id, fallbackDescending: false)
            .Select(r => r.Id);

        Assert.Equal([1, 2, 3], ordered);
    }

    [Fact]
    public void OrderByProperty_HonoursTheFallbackDirection()
    {
        Assert.Equal(
            [3, 2, 1],
            Query.OrderByProperty(null, descending: false, r => r.Id, fallbackDescending: true).Select(r => r.Id));
    }
}
