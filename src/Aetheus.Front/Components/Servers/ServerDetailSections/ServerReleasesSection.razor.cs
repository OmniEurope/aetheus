// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.ServerDetailSections;

// 0-a: thin wrapper around the shared ReleasesList (server scope). Data fetch + rendering live in
// the shared component, so the server detail no longer keeps its own diverged grid.
public partial class ServerReleasesSection
{
    [Parameter, EditorRequired] public int ServerId { get; set; }
}
