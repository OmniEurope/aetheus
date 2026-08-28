// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

// 0-a: thin wrapper around the shared VaultsList (server scope).
public partial class ServerVaultsSection
{
    [Parameter, EditorRequired] public int ServerId { get; set; }
}
