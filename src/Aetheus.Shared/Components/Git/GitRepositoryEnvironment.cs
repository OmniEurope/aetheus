// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Git;

/// <summary>
/// The environment variables through which git binds a process to a specific repository. A git hook
/// hands them to every child process, so a git command meant for another directory (a test fixture, a
/// pipeline workspace) silently operates on the hooking repository instead: on 2026-09-17 a pre-push
/// hook run from a linked worktree kept the common directory, and fixture commands rewrote the real
/// branches and force-pushed a fixture commit to the production origin's main.
/// </summary>
public static class GitRepositoryEnvironment
{
    /// <summary>Mirrors <c>git rev-parse --local-env-vars</c> (a regression test keeps them aligned).</summary>
    public static IReadOnlyList<string> RepositoryLocalVariables { get; } =
    [
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_CONFIG",
        "GIT_CONFIG_PARAMETERS",
        "GIT_CONFIG_COUNT",
        "GIT_OBJECT_DIRECTORY",
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_IMPLICIT_WORK_TREE",
        "GIT_GRAFT_FILE",
        "GIT_INDEX_FILE",
        "GIT_NO_REPLACE_OBJECTS",
        "GIT_REPLACE_REF_BASE",
        "GIT_PREFIX",
        "GIT_SHALLOW_FILE",
        "GIT_COMMON_DIR"
    ];

    /// <summary>
    /// True for a repository-local variable, including the numbered <c>GIT_CONFIG_KEY_n</c> /
    /// <c>GIT_CONFIG_VALUE_n</c> pairs that <c>GIT_CONFIG_COUNT</c> activates (they can rewrite any
    /// setting, a remote URL included) and which git does not list itself.
    /// </summary>
    public static bool IsRepositoryLocal(string name) =>
        RepositoryLocalVariables.Contains(name, StringComparer.OrdinalIgnoreCase)
        || name.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes every repository-local git variable from a child process environment.</summary>
    public static void Neutralize(IDictionary<string, string?> environment)
    {
        foreach (var name in environment.Keys.Where(IsRepositoryLocal).ToList())
            environment.Remove(name);
    }
}
