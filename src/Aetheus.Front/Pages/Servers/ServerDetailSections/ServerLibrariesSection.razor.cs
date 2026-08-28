// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

// 0-a: thin wrapper around the shared VariableLibrariesList (server scope).
public partial class ServerLibrariesSection
{
    [Parameter, EditorRequired] public int ServerId { get; set; }
}
