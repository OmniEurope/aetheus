// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Data;

internal static class UserQueryExtensions
{
    public static IQueryable<User> IncludeRoles(this IQueryable<User> query) => query
        .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role);

    public static Task<User?> FindByUsernameWithRolesAsync(
        this IQueryable<User> query,
        string username,
        CancellationToken ct = default)
    {
        var normalizedUsername = username.Trim().ToLower();
        return query
            .AsNoTracking()
            .IncludeRoles()
            .FirstOrDefaultAsync(user => user.Username.ToLower() == normalizedUsername, ct);
    }
}
