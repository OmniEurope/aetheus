// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Git;

internal static class GitLightCollectionPager
{
    public static PaginatedResult<GitLightBranchDto> Branches(
        IEnumerable<GitLightBranchDto> source, PaginationRequest request) =>
        Page(Filter(source, request.Search, x => x.Name), request,
            x => x.Name, x => x.LastCommitDate);

    public static PaginatedResult<GitLightTagDto> Tags(
        IEnumerable<GitLightTagDto> source, PaginationRequest request) =>
        Page(Filter(source, request.Search, x => $"{x.Name} {x.Message}"), request,
            x => x.Name, x => x.TaggerDate);

    public static PaginatedResult<GitLightTreeEntryDto> Tree(
        IEnumerable<GitLightTreeEntryDto> source, PaginationRequest request) =>
        Page(Filter(source, request.Search, x => x.Name), request,
            x => x.Name, x => x.Size);

    public static PaginatedResult<BranchProtectionRuleDto> Protection(
        IEnumerable<BranchProtectionRuleDto> source, PaginationRequest request) =>
        Page(Filter(source, request.Search, x => x.Pattern), request,
            x => x.Pattern, x => x.Id);

    private static IEnumerable<T> Filter<T>(
        IEnumerable<T> source, string? search, Func<T, string?> text) =>
        string.IsNullOrWhiteSpace(search)
            ? source
            : source.Where(item => text(item)?.Contains(
                search, StringComparison.OrdinalIgnoreCase) == true);

    private static PaginatedResult<T> Page<T, TSecondary>(
        IEnumerable<T> source, PaginationRequest request,
        Func<T, string> name, Func<T, TSecondary> secondary)
    {
        var (page, pageSize) = request.Normalize();
        var items = source.ToList();
        var ordered = request.SortBy switch
        {
            "LastCommitDate" or "TaggerDate" or "Size" or "Id" =>
                request.SortDescending
                    ? items.OrderByDescending(secondary)
                    : items.OrderBy(secondary),
            _ => request.SortDescending
                ? items.OrderByDescending(name, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(name, StringComparer.OrdinalIgnoreCase)
        };
        return new PaginatedResult<T>
        {
            Items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = items.Count,
            Page = page,
            PageSize = pageSize
        };
    }
}
