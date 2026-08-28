// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

/// <summary>
/// Scope of a Personal Access Token (ADR-024 4.5). The scope narrows what a PAT can do; it is
/// always re-intersected with the bearer's CURRENT RBAC permissions at request time, so a PAT can
/// never grant more than the user currently holds.
/// </summary>
public enum PatScope
{
    /// <summary>Read-only: safe HTTP methods (GET/HEAD/OPTIONS) only. Any mutating request is refused 403.</summary>
    ReadOnly = 0,

    /// <summary>Read and write, bounded by the user's live RBAC permissions (same as an interactive session).</summary>
    ReadWrite = 1
}
