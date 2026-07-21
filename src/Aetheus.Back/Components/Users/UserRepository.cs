// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Users;

public class UserRepository(AppDbContext db) : IUserRepository
{
    public async Task<(List<User> Items, int TotalCount)> GetUsersPagedAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Username.Contains(search) || (u.Email != null && u.Email.Contains(search)));

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .OrderBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<(List<UserDto> Items, int TotalCount)> GetUsersPagedProjectedAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Username.Contains(search) || (u.Email != null && u.Email.Contains(search)));

        var totalCount = await query.CountAsync(ct).ConfigureAwait(false);

        var items = await query
            .OrderBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsActive = u.IsActive,
                TotpEnabled = u.TotpEnabled,
                Roles = u.UserRoles.Select(ur => ur.Role.Name).ToList(),
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<User?> GetUserDetailAsync(int id, CancellationToken ct = default)
    {
        return await db.Users
            .Where(u => u.Id == id)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        return await db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == username, ct)
            .ConfigureAwait(false);
    }

    public async Task<User?> FindUserAsync(int id, CancellationToken ct = default)
    {
        return await db.Users.FindAsync([id], ct).ConfigureAwait(false);
    }

    public async Task AddUserAsync(User user, CancellationToken ct = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveUserAsync(User user, CancellationToken ct = default)
    {
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public Task<int> CountActiveAdminsAsync(CancellationToken ct = default)
    {
        return db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Name == "Admin"))
            .CountAsync(ct);
    }

    public async Task<List<Role>> GetRolesByNamesAsync(List<string> roleNames, CancellationToken ct = default)
    {
        return await db.Roles
            .AsNoTracking()
            .Where(r => roleNames.Contains(r.Name))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Role?> FindRoleAsync(int roleId, CancellationToken ct = default)
    {
        return await db.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId, ct)
            .ConfigureAwait(false);
    }

    public async Task<List<string>> GetAllRoleNamesAsync(CancellationToken ct = default)
    {
        return await db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => r.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
