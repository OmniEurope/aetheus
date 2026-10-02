// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Shared;

/// <summary>Shared bounds for every server-paginated collection.</summary>
public static class PaginationDefaults
{
    public const int MinimumPageSize = 1;
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 200;

    public static int Clamp(int pageSize) =>
        Math.Clamp(pageSize, MinimumPageSize, MaximumPageSize);
}
