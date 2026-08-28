// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.ServiceConnections;

public class ServiceConnectionService(
    IServiceConnectionRepository repo,
    IEncryptionService encryption,
    IAuditService audit,
    IEntityChangeNotifier notifier,
    IServiceConnectionTester tester,
    TimeProvider timeProvider) : IServiceConnectionService
{
    public async Task<PaginatedResult<ServiceConnectionDto>> GetConnectionsAsync(int? projectId, PaginationRequest request, List<int>? accessibleIds = null, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        var (items, totalCount) = await repo.GetPagedAsync(
            request.Search, projectId, page, pageSize, accessibleIds, ct,
            request.SortBy, request.SortDescending).ConfigureAwait(false);

        return new PaginatedResult<ServiceConnectionDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ServiceConnectionDetailDto?> GetConnectionAsync(int id, CancellationToken ct = default)
    {
        var connection = await repo.GetDetailAsync(id, ct).ConfigureAwait(false);
        if (connection is null) return null;

        return new ServiceConnectionDetailDto
        {
            Id = connection.Id,
            Name = connection.Name,
            Description = connection.Description,
            Type = connection.Type,
            ProjectId = connection.ProjectId,
            ProjectName = connection.Project?.Name,
            Url = connection.Url,
            ConfigurationJson = SensitiveConfigurationJson.MaskSecrets(
                encryption.DecryptValue(connection.EncryptedPayload)),
            CreatedAt = connection.CreatedAt,
            UpdatedAt = connection.UpdatedAt
        };
    }

    public async Task<ServiceConnectionDto> CreateConnectionAsync(CreateServiceConnectionRequest request, CancellationToken ct = default)
    {
        var connection = new ServiceConnection
        {
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            ProjectId = request.ProjectId,
            Url = request.Url,
            EncryptedPayload = encryption.EncryptValue(request.ConfigurationJson)
        };

        await repo.AddAsync(connection, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "ServiceConnection", connection.Id, connection.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.ServiceConnection, connection.Id, EntityChangeOps.Created, ct).ConfigureAwait(false);
        return MapToDto(connection);
    }

    public async Task<ServiceConnectionDto?> UpdateConnectionAsync(int id, UpdateServiceConnectionRequest request, CancellationToken ct = default)
    {
        var connection = await repo.FindAsync(id, ct).ConfigureAwait(false);
        if (connection is null) return null;

        connection.Name = request.Name;
        connection.Description = request.Description;
        connection.ProjectId = request.ProjectId;
        connection.Url = request.Url;
        var existingConfiguration = encryption.DecryptValue(connection.EncryptedPayload);
        var restoredConfiguration = SensitiveConfigurationJson.RestoreMaskedSecrets(
            existingConfiguration,
            request.ConfigurationJson);
        connection.EncryptedPayload = encryption.EncryptValue(restoredConfiguration);
        connection.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "ServiceConnection", connection.Id, connection.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.ServiceConnection, connection.Id, EntityChangeOps.Updated, ct).ConfigureAwait(false);
        return MapToDto(connection);
    }

    public async Task<bool> DeleteConnectionAsync(int id, CancellationToken ct = default)
    {
        var connection = await repo.FindAsync(id, ct).ConfigureAwait(false);
        if (connection is null) return false;

        await repo.RemoveAsync(connection, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "ServiceConnection", id, connection.Name, ct).ConfigureAwait(false);
        await notifier.BroadcastAsync(ResourceType.ServiceConnection, id, EntityChangeOps.Deleted, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<ServiceConnectionTestResultDto?> TestConnectionAsync(int id, CancellationToken ct = default)
    {
        var connection = await repo.FindAsync(id, ct).ConfigureAwait(false);
        if (connection is null) return null;

        var config = encryption.DecryptValue(connection.EncryptedPayload);
        return await tester.TestAsync(connection.Type, connection.Url, config, ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, string>> ResolveConnectionSecretsAsync(List<string> names, int? projectId, CancellationToken ct = default)
    {
        var connections = await repo.FindByNamesAsync(names, projectId, ct).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var conn in connections)
        {
            var prefix = $"SVC_{conn.Name.ToUpperInvariant().Replace(' ', '_')}";
            result[$"{prefix}_URL"] = conn.Url ?? string.Empty;
            result[$"{prefix}_TYPE"] = conn.Type.ToString();

            var decrypted = encryption.DecryptValue(conn.EncryptedPayload);
            result[$"{prefix}_CONFIG"] = decrypted;
        }

        return result;
    }

    private static ServiceConnectionDto MapToDto(ServiceConnection sc) => new()
    {
        Id = sc.Id,
        Name = sc.Name,
        Description = sc.Description,
        Type = sc.Type,
        ProjectId = sc.ProjectId,
        ProjectName = sc.Project?.Name,
        Url = sc.Url,
        CreatedAt = sc.CreatedAt,
        UpdatedAt = sc.UpdatedAt
    };
}
