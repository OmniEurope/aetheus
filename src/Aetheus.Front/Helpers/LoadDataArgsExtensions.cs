// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.Constants;
using Radzen;

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
}
