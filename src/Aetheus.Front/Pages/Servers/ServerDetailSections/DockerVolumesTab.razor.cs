// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

/// <summary>
/// The Docker volumes grid. Read-only over the parent's inventory, with only its own filter state -
/// see <see cref="DockerNetworksTab"/> for why the ownership runs that way.
/// </summary>
public partial class DockerVolumesTab
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<DockerVolumeDto> Volumes { get; set; } = [];

    private string _volumeSearch = string.Empty;

    private IList<GroupDescriptor> _volumeGroups =
    [
        new GroupDescriptor { Property = nameof(DockerVolumeDto.Project), Title = "Project" }
    ];

    private bool? _allVolumeGroupsExpanded = false;

    private List<DockerVolumeDto> FilteredVolumes => Volumes
        .Where(v => string.IsNullOrWhiteSpace(_volumeSearch)
            || v.Name.Contains(_volumeSearch, StringComparison.OrdinalIgnoreCase)
            || v.Driver.Contains(_volumeSearch, StringComparison.OrdinalIgnoreCase))
        .ToList();
}
