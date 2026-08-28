// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Services;

public class PermissionService
{
    private List<EffectivePermissionDto> _permissions = [];
    private bool _isAdmin;

    public bool IsLoaded { get; private set; }

    public event Action? OnPermissionsChanged;

    public void SetPermissions(List<EffectivePermissionDto> permissions, bool isAdmin)
    {
        _permissions = permissions;
        _isAdmin = isAdmin;
        IsLoaded = true;
        OnPermissionsChanged?.Invoke();
    }

    public void Clear()
    {
        _permissions = [];
        _isAdmin = false;
        IsLoaded = false;
        OnPermissionsChanged?.Invoke();
    }

    public bool CanRead(ResourceType resourceType, int? resourceId = null)
        => HasPermission(resourceType, resourceId, Permission.Read);

    public bool CanWrite(ResourceType resourceType, int? resourceId = null)
        => HasPermission(resourceType, resourceId, Permission.Write);

    public bool CanAdmin(ResourceType resourceType, int? resourceId = null)
        => HasPermission(resourceType, resourceId, Permission.Admin);

    public bool CanReadAny(ResourceType resourceType)
        => _isAdmin || _permissions.Any(p =>
            p.ResourceType == resourceType
            && p.Permission >= Permission.Read);

    public bool HasPermission(ResourceType resourceType, int? resourceId, Permission required)
    {
        if (_isAdmin) return true;

        foreach (var perm in _permissions)
        {
            if (perm.ResourceType != resourceType) continue;
            if ((int)perm.Permission < (int)required) continue;

            // Wildcard (null ResourceId) covers all resources
            if (perm.ResourceId is null) return true;

            // Specific resource match
            if (resourceId.HasValue && perm.ResourceId == resourceId.Value) return true;
        }

        return false;
    }

    public List<EffectivePermissionDto> GetPermissions() => _permissions;
}
