// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Services;

public interface ISecretMaskingService
{
    Task<string> MaskAsync(string message, int? pipelineRunId, CancellationToken ct = default);
    void EvictCache(int pipelineRunId);

    /// <summary>Registers a secret minted at runtime (not declared in the pipeline vault YAML) so it is
    /// masked in this run's logs too (e.g. the ephemeral Git clone token). Defense in depth: the value
    /// should never be echoed, but if it leaks into a log line it must still be redacted.</summary>
    void RegisterRuntimeSecret(int pipelineRunId, string secret);
}
