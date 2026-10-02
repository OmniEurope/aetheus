// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.CompilerServices;

namespace Aetheus.Back.Tests;

/// <summary>
/// Strips every repository-local git variable from this test process before any test runs. The suite
/// is launched by the pre-push hook, which hands its repository to every child process; any git
/// command a test (or the code under test) spawns without neutralizing its own environment would then
/// operate on the real repository. On 2026-09-17 that path rewrote local branches and force-pushed a
/// fixture commit to the production origin's main. Per-call neutralization stays; this closes the gaps.
/// </summary>
internal static class GitEnvironmentIsolation
{
#pragma warning disable CA2255 // A test assembly must isolate its whole process before the first test.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void StripInheritedRepositoryVariables()
    {
        var inherited = Environment.GetEnvironmentVariables().Keys.Cast<object>()
            .Select(key => key.ToString()!)
            .Where(GitRepositoryEnvironment.IsRepositoryLocal)
            .ToList();
        foreach (var name in inherited)
            Environment.SetEnvironmentVariable(name, null);
    }
}
