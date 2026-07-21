// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Caching.Memory;

namespace Aetheus.Back.Services;

public sealed class SecretMaskingService(ISecretMaskingRepository secretRepo, IEncryptionService encryption, IMemoryCache cache) : ISecretMaskingService
{
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(30);

    public async Task<string> MaskAsync(string message, int? pipelineRunId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(message) || pipelineRunId is null)
            return message;

        var secrets = await GetSecretsForRunAsync(pipelineRunId.Value, ct).ConfigureAwait(false);
        var runtimeSecrets = GetRuntimeSecrets(pipelineRunId.Value);
        if (secrets.Count == 0 && runtimeSecrets.Count == 0)
            return message;

        // Mask runtime-registered secrets (e.g. the ephemeral clone token) as well as vault secrets;
        // both are already filtered (length >= 4) and sorted longest-first by their loaders.
        var result = message;
        foreach (var secret in runtimeSecrets)
            result = result.Replace(secret, "***", StringComparison.Ordinal);
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
                result = result.Replace(secret, "***", StringComparison.Ordinal);
        }

        return result;
    }

    public void EvictCache(int pipelineRunId)
    {
        cache.Remove(CacheKey(pipelineRunId));
        cache.Remove(RuntimeCacheKey(pipelineRunId));
    }

    public void RegisterRuntimeSecret(int pipelineRunId, string secret)
    {
        // F-23 parity: don't register tiny values that would mangle every log line.
        if (string.IsNullOrEmpty(secret) || secret.Length < 4)
            return;

        var key = RuntimeCacheKey(pipelineRunId);
        var list = cache.GetOrCreate(key, entry =>
        {
            entry.SlidingExpiration = SlidingExpiration;
            return new List<string>();
        })!;
        if (!list.Contains(secret))
        {
            list.Add(secret);
            // Longest-first so a secret that is a substring of another is masked correctly.
            list.Sort((a, b) => b.Length.CompareTo(a.Length));
        }
    }

    private List<string> GetRuntimeSecrets(int pipelineRunId) =>
        cache.TryGetValue<List<string>>(RuntimeCacheKey(pipelineRunId), out var list) && list is not null
            ? list
            : [];

    private static string CacheKey(int pipelineRunId) => $"secret-masking:{pipelineRunId}";

    private static string RuntimeCacheKey(int pipelineRunId) => $"secret-masking-runtime:{pipelineRunId}";

    private async Task<List<string>> GetSecretsForRunAsync(int pipelineRunId, CancellationToken ct)
    {
        var key = CacheKey(pipelineRunId);

        if (cache.TryGetValue<List<string>>(key, out var cached) && cached is not null)
            return cached;

        var pipelineId = await secretRepo.GetPipelineIdForRunAsync(pipelineRunId, ct).ConfigureAwait(false);
        if (pipelineId is null)
            return [];

        var (yamlDefinition, projectId) = await secretRepo.GetPipelineYamlAndProjectAsync(pipelineId.Value, ct).ConfigureAwait(false);
        if (yamlDefinition is null)
            return [];

        var vaultNames = YamlParsingHelper.ParseVaultNames(yamlDefinition);
        if (vaultNames.Count == 0)
            return [];

        var encryptedValues = await secretRepo.GetEncryptedSecretsAsync(vaultNames, projectId, ct).ConfigureAwait(false);

        var decrypted = new List<string>(encryptedValues.Count);
        foreach (var enc in encryptedValues)
        {
            var plain = encryption.DecryptValue(enc);
            // F-23: refuse to mask very short secret values to avoid sweeping false-positives
            // (e.g. a 2-char secret would mangle every log line).
            if (!string.IsNullOrEmpty(plain) && plain.Length >= 4)
                decrypted.Add(plain);
        }

        // Sort longest-first for correct masking
        decrypted.Sort((a, b) => b.Length.CompareTo(a.Length));

        cache.Set(key, decrypted, new MemoryCacheEntryOptions { SlidingExpiration = SlidingExpiration });
        return decrypted;
    }
}
