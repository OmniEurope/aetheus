// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class DockerProjectDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter, EditorRequired] public string Project { get; set; } = string.Empty;

    private List<DockerContainerDto> Containers => Server.Docker.Containers.Where(item => item.Project == Project).ToList();
    private List<DockerImageDto> Images => Server.Docker.Images.Where(item => item.Project == Project).ToList();
    private List<DockerNetworkDto> Networks => Server.Docker.Networks.Where(item => item.Project == Project).ToList();
    private List<DockerVolumeDto> Volumes => Server.Docker.Volumes.Where(item => item.Project == Project).ToList();
    private List<DockerComposeStackDto> ComposeStacks => Server.Docker.ComposeStacks.Where(item => string.Equals(item.Name, Project, StringComparison.OrdinalIgnoreCase)).ToList();

    private void CloseWithAction(DockerContainerAction action) => Dialog.Close(action);
}
