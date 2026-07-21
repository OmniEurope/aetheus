// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Services;

public sealed class SecretMaskingRepository(AppDbContext db) : ISecretMaskingRepository
{
    public async Task<int?> GetPipelineIdForRunAsync(int pipelineRunId, CancellationToken ct = default)
    {
        return await db.PipelineRuns
            .AsNoTracking()
            .Where(r => r.Id == pipelineRunId)
            .Select(r => (int?)r.PipelineId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<(string? YamlDefinition, int? ProjectId)> GetPipelineYamlAndProjectAsync(int pipelineId, CancellationToken ct = default)
    {
        var result = await db.Pipelines
            .AsNoTracking()
            .Where(p => p.Id == pipelineId)
            .Select(p => new { p.YamlDefinition, p.ProjectId })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return result is null ? (null, null) : (result.YamlDefinition, result.ProjectId);
    }

    public async Task<List<string>> GetEncryptedSecretsAsync(List<string> vaultNames, int? projectId, CancellationToken ct = default)
    {
        return await db.VaultSecrets
            .AsNoTracking()
            .Where(vs => vaultNames.Contains(vs.Vault.Name)
                && (vs.Vault.ProjectId == projectId || vs.Vault.ProjectId == null))
            .Select(vs => vs.EncryptedValue)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
