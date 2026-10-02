// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Releases;

/// <summary>Lifecycle of a manual release rollback request. A request becomes successful only after
/// the deployment agent reports a health-gated deploy success; creating the request never changes a
/// release status by itself.</summary>
public enum RollbackStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}
