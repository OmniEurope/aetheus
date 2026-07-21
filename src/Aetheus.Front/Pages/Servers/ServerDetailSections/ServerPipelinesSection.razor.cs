// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

// 0-a: thin wrapper around the shared PipelinesList (server scope). Data fetch + rendering live in
// the shared component, so the server detail no longer keeps its own diverged grid.
public partial class ServerPipelinesSection
{
    [Parameter, EditorRequired] public int ServerId { get; set; }
}
