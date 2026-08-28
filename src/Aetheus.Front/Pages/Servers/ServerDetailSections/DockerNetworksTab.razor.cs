// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

/// <summary>
/// The Docker networks grid.
///
/// It reads <see cref="Networks"/> and never mutates it: the parent owns the refresh cycle and the
/// SignalR wiring, so a tab that wrote back would have two owners for one list. Its search text and
/// group expansion are local by design - they are view state nobody else needs, and keeping them here
/// is what let the parent shed five tabs' worth of fields.
/// </summary>
public partial class DockerNetworksTab
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<DockerNetworkDto> Networks { get; set; } = [];

    private string _networkSearch = string.Empty;

    private IList<GroupDescriptor> _networkGroups =
    [
        new GroupDescriptor { Property = nameof(DockerNetworkDto.Project), Title = "Project" }
    ];

    // Start collapsed. Two-way bound so individual group toggling still works after the first render.
    private bool? _allNetworkGroupsExpanded = false;

    private List<DockerNetworkDto> FilteredNetworks => Networks
        .Where(n => string.IsNullOrWhiteSpace(_networkSearch)
            || n.Name.Contains(_networkSearch, StringComparison.OrdinalIgnoreCase)
            || n.Driver.Contains(_networkSearch, StringComparison.OrdinalIgnoreCase))
        .ToList();
}
