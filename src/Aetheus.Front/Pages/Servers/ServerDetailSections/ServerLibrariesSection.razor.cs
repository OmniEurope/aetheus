// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

// 0-a: thin wrapper around the shared VariableLibrariesList (server scope).
public partial class ServerLibrariesSection
{
    [Parameter, EditorRequired] public int ServerId { get; set; }
}
