// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Helpers;

/// <summary>
/// Helpers for converting Radzen <see cref="LoadDataArgs"/> into 1-based page / page-size
/// pairs expected by Aetheus paginated API endpoints.
/// </summary>
public static class LoadDataArgsExtensions
{
    public const int DefaultPageSize = PaginationDefaults.DefaultPageSize;

    /// <summary>Returns 1-based page number computed from <c>Skip</c>/<c>Top</c>.</summary>
    public static int GetPage(this LoadDataArgs args, int defaultPageSize = DefaultPageSize)
    {
        var top = args.GetPageSize(defaultPageSize);
        return ((args.Skip ?? 0) / top) + 1;
    }

    /// <summary>Returns the page size, falling back to the default.</summary>
    public static int GetPageSize(this LoadDataArgs args, int defaultPageSize = DefaultPageSize)
        => PaginationDefaults.Clamp(args.Top ?? PaginationDefaults.Clamp(defaultPageSize));

    /// <summary>Returns a <c>(page, pageSize)</c> pair.</summary>
    public static (int Page, int PageSize) ToPageRequest(this LoadDataArgs args, int defaultPageSize = DefaultPageSize)
        => (args.GetPage(defaultPageSize), args.GetPageSize(defaultPageSize));

    /// <summary>Returns a page from an already loaded collection, honouring the grid's sort and column
    /// filters.
    /// <para>The whole collection is in memory here, so both are applied to all of it before the page
    /// is cut. Paging the raw list first, as this used to, made the header affordances decorative: the
    /// filter popups opened and the sort arrows moved while the same rows came back in the same
    /// order.</para></summary>
    public static List<T> ToClientPage<T>(
        this LoadDataArgs args,
        IReadOnlyList<T> source,
        int defaultPageSize = DefaultPageSize)
    {
        var rows = ApplyClientSort(ApplyClientFilters(source, args), args);
        var skip = args.Skip ?? 0;
        return rows.Skip(skip).Take(args.GetPageSize(defaultPageSize)).ToList();
    }

    /// <summary>Number of rows left once the grid's column filters are applied. Callers use it for the
    /// pager count, so the pager measures what is reachable rather than the unfiltered collection.
    /// </summary>
    public static int ClientFilteredCount<T>(this LoadDataArgs args, IReadOnlyList<T> source)
        => ApplyClientFilters(source, args).Count();

    private static IEnumerable<T> ApplyClientFilters<T>(IReadOnlyList<T> source, LoadDataArgs args)
    {
        if (args.Filters is null) return source;

        IEnumerable<T> rows = source;
        foreach (var filter in args.Filters)
        {
            if (filter.FilterValue is null || string.IsNullOrWhiteSpace(filter.Property)) continue;
            var property = typeof(T).GetProperty(filter.Property);
            // A filter naming a property this row type does not have narrows nothing. Dropping the row
            // set instead would show an empty table for a column the user can see data in.
            if (property is null) continue;

            var needle = filter.FilterValue.ToString();
            if (string.IsNullOrWhiteSpace(needle)) continue;

            rows = rows.Where(row =>
            {
                var value = property.GetValue(row)?.ToString();
                return value is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase);
            });
        }

        return rows;
    }

    private static IEnumerable<T> ApplyClientSort<T>(IEnumerable<T> rows, LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return rows;

        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var property = typeof(T).GetProperty(parts[0]);
        if (property is null) return rows;

        var descending = parts.Length > 1 && string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase);
        return descending
            ? rows.OrderByDescending(row => property.GetValue(row))
            : rows.OrderBy(row => property.GetValue(row));
    }

    /// <summary>Returns the first Radzen sort column and direction, or the supplied fallback.</summary>
    public static (string SortBy, bool Descending) ToSortRequest(
        this LoadDataArgs args,
        string fallback,
        bool fallbackDescending = false)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return (fallback, fallbackDescending);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1
            && string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns the value the user typed into one column's header filter, or null when that
    /// column is unfiltered. Property names are matched case-insensitively so a caller can pass the
    /// DTO property name regardless of how the column declared it.</summary>
    public static string? ColumnFilter(this LoadDataArgs args, string property)
    {
        var value = args.Filters?
            .FirstOrDefault(f => string.Equals(f.Property, property, StringComparison.OrdinalIgnoreCase))?
            .FilterValue?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Same as <see cref="ColumnFilter"/> for a column bound to an enum. Returns null when the
    /// column is unfiltered or when the value does not name a member, so an unparsable filter narrows
    /// nothing instead of silently returning an empty page.</summary>
    public static TEnum? ColumnFilter<TEnum>(this LoadDataArgs args, string property) where TEnum : struct, Enum
    {
        var raw = args.Filters?
            .FirstOrDefault(f => string.Equals(f.Property, property, StringComparison.OrdinalIgnoreCase))?
            .FilterValue;
        return raw switch
        {
            null => null,
            TEnum typed => typed,
            _ => Enum.TryParse<TEnum>(raw.ToString(), ignoreCase: true, out var parsed) ? parsed : null
        };
    }
}
