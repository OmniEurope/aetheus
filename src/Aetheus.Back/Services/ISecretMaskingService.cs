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

    /// <summary>The runtime secret VALUES known for this run, for callers that must redact a value they
    /// already hold rather than a log line. Synchronous on purpose: condition evidence is captured on a
    /// synchronous path, and a value that reaches persistence unredacted is served unredacted forever.
    /// Returns an empty set when nothing was registered.</summary>
    IReadOnlyCollection<string> GetRuntimeSecretValues(int pipelineRunId);
}
