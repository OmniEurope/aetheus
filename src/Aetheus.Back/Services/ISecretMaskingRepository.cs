// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services;

public interface ISecretMaskingRepository
{
    Task<int?> GetPipelineIdForRunAsync(int pipelineRunId, CancellationToken ct = default);
    Task<(string? YamlDefinition, int? ProjectId)> GetPipelineYamlAndProjectAsync(int pipelineId, CancellationToken ct = default);
    Task<List<string>> GetEncryptedSecretsAsync(List<string> vaultNames, int? projectId, CancellationToken ct = default);
}
