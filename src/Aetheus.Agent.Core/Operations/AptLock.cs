// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// apt/dpkg hold a system-wide lock (<c>/var/lib/dpkg/lock-frontend</c>): two concurrent apt operations
/// (the agent runs up to MaxConcurrentTasks in parallel) collide with "Could not get lock" and exit 100.
/// This single process-wide gate serialises every apt op - install, remove, AND upgrade - so they run
/// back-to-back. Shared by <see cref="PackageOperationExecutor"/> and <c>SystemPackageUpgradeExecutor</c>.
/// </summary>
internal static class AptLock
{
    public static readonly SemaphoreSlim Gate = new(1, 1);
}
