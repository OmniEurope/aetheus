// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Services;

public class PermissionRepository(AppDbContext db) : IPermissionRepository
{
    public async Task<List<int>> GetRoleIdsForUserAsync(string username, CancellationToken ct = default)
    {
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Username == username && u.IsActive)
            .SelectMany(u => u.UserRoles)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> HasPermissionAsync(List<int> roleIds, ResourceType resourceType, int? resourceId, Permission required, CancellationToken ct = default)
    {
        return await db.ResourcePermissions
            .AsNoTracking()
            .AnyAsync(rp =>
                roleIds.Contains(rp.RoleId)
                && rp.ResourceType == resourceType
                && (rp.ResourceId == null || rp.ResourceId == resourceId)
                && rp.Permission >= required, ct)
            .ConfigureAwait(false);
    }

    public async Task<List<int>?> GetAccessibleResourceIdsAsync(List<int> roleIds, ResourceType resourceType, Permission required, CancellationToken ct = default)
    {
        var hasWildcard = await db.ResourcePermissions
            .AsNoTracking()
            .AnyAsync(rp =>
                roleIds.Contains(rp.RoleId)
                && rp.ResourceType == resourceType
                && rp.ResourceId == null
                && rp.Permission >= required, ct)
            .ConfigureAwait(false);

        if (hasWildcard) return null;

        return await db.ResourcePermissions
            .AsNoTracking()
            .Where(rp =>
                roleIds.Contains(rp.RoleId)
                && rp.ResourceType == resourceType
                && rp.ResourceId != null
                && rp.Permission >= required)
            .Select(rp => rp.ResourceId!.Value)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<int>> GetOrganizationIdsForUsernameAsync(string username, CancellationToken ct = default)
    {
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Username == username && u.IsActive)
            .SelectMany(u => db.OrganizationMembers.Where(m => m.UserId == u.Id))
            .Select(m => m.OrganizationId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<int>> GetResourceIdsByOrganizationsAsync(ResourceType resourceType, List<int> orgIds, CancellationToken ct = default)
    {
        if (orgIds.Count == 0) return [];
        return resourceType switch
        {
            ResourceType.Server => await db.Servers.AsNoTracking()
                .Where(s => orgIds.Contains(s.OrganizationId))
                .Select(s => s.Id)
                .ToListAsync(ct).ConfigureAwait(false),
            ResourceType.Project => await db.Projects.AsNoTracking()
                .Where(p => orgIds.Contains(p.OrganizationId))
                .Select(p => p.Id)
                .ToListAsync(ct).ConfigureAwait(false),
            ResourceType.PipelineTemplate => await db.PipelineTemplates.AsNoTracking()
                .Where(template => orgIds.Contains(template.OrganizationId))
                .Select(template => template.Id)
                .ToListAsync(ct).ConfigureAwait(false),
            _ => []
        };
    }

    public async Task<bool> IsUserInResourceOrganizationAsync(string username, ResourceType resourceType, int resourceId, CancellationToken ct = default)
    {
        var orgIdQuery = resourceType switch
        {
            ResourceType.Server => db.Servers.AsNoTracking()
                .Where(s => s.Id == resourceId).Select(s => (int?)s.OrganizationId),
            ResourceType.Project => db.Projects.AsNoTracking()
                .Where(p => p.Id == resourceId).Select(p => (int?)p.OrganizationId),
            ResourceType.PipelineTemplate => db.PipelineTemplates.AsNoTracking()
                .Where(template => template.Id == resourceId).Select(template => (int?)template.OrganizationId),
            _ => null
        };
        if (orgIdQuery is null) return false;
        var orgId = await orgIdQuery.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (orgId is null) return false;

        return await db.Users.AsNoTracking()
            .Where(u => u.Username == username && u.IsActive)
            .SelectMany(u => db.OrganizationMembers.Where(m => m.UserId == u.Id && m.OrganizationId == orgId))
            .AnyAsync(ct)
            .ConfigureAwait(false);
    }
}
