// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.ServerModules;

public class ServerModuleService(IServerModuleRepository repo, IAuditService audit, TimeProvider timeProvider) : IServerModuleService
{
    public async Task<List<ServerModuleDto>> GetByServerIdAsync(int serverId, CancellationToken ct = default)
    {
        var modules = await repo.GetByServerIdAsync(serverId, ct).ConfigureAwait(false);
        return modules.Select(MapToDto).ToList();
    }

    public async Task<ServerModuleDto?> GetByIdAsync(int serverId, int id, CancellationToken ct = default)
    {
        var module = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (module is null || module.ServerId != serverId) return null;
        return MapToDto(module);
    }

    public async Task<ServerModuleDto> CreateAsync(int serverId, CreateServerModuleRequest request, CancellationToken ct = default)
    {
        var module = new ServerModule
        {
            ServerId = serverId,
            Name = request.Name,
            Type = request.Type,
            Version = request.Version,
            Configuration = request.Configuration ?? "{}"
        };

        await repo.AddAsync(module, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "ServerModule", module.Id, module.Name, ct).ConfigureAwait(false);
        return MapToDto(module);
    }

    public async Task<ServerModuleDto?> UpdateAsync(int serverId, int id, UpdateServerModuleRequest request, CancellationToken ct = default)
    {
        var module = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (module is null || module.ServerId != serverId) return null;

        module.Name = request.Name;
        module.Status = request.Status;
        module.Version = request.Version;
        module.Configuration = request.Configuration ?? "{}";
        module.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "ServerModule", module.Id, module.Name, ct).ConfigureAwait(false);
        return MapToDto(module);
    }

    public async Task<bool> DeleteAsync(int serverId, int id, CancellationToken ct = default)
    {
        var module = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (module is null || module.ServerId != serverId) return false;

        var name = module.Name;
        await repo.RemoveAsync(module, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "ServerModule", id, name, ct).ConfigureAwait(false);
        return true;
    }

    private static ServerModuleDto MapToDto(ServerModule m) => new()
    {
        Id = m.Id,
        ServerId = m.ServerId,
        Name = m.Name,
        Type = m.Type,
        Status = m.Status,
        Version = m.Version,
        InstalledAt = m.InstalledAt,
        UpdatedAt = m.UpdatedAt
    };
}
