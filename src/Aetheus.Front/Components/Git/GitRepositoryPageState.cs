// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Git;

internal sealed class GitRepositoryPageState
{
    public int TotalCount { get; set; }
    public bool Loading { get; set; }
    public bool Error { get; set; }
}
