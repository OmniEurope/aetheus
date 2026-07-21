// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Collectors;

/// <summary>
/// S-TECH-15: hashes the agent's sudoers drop-in files so the backend can detect
/// tampering. Returns SHA256 hex digests keyed by file name; empty when no files
/// exist or are unreadable (Windows, no sudo grants, or insufficient permissions).
/// </summary>
public interface ISudoersHashCollector
{
    Task<Dictionary<string, string>> CollectAsync(CancellationToken ct = default);
}
