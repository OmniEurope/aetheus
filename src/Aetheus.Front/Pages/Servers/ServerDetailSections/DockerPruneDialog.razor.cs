// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class DockerPruneDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private DockerPruneDialogModel _model = new();
    private bool SelectAll => _model.Containers && _model.Images && _model.Volumes;
    private int SelectedCount => (_model.Containers ? 1 : 0) + (_model.Images ? 1 : 0) + (_model.Volumes ? 1 : 0);
    private void SetAll(bool value) => _model = new DockerPruneDialogModel { Containers = value, Images = value, Volumes = value };
    private void Submit(DockerPruneDialogModel model) => Dialog.Close(new DockerPruneDialogResult(model.Containers, model.Images, model.Volumes));
}

public sealed class DockerPruneDialogModel
{
    public bool Containers { get; set; } = true;
    public bool Images { get; set; } = true;
    public bool Volumes { get; set; } = true;
}

public sealed record DockerPruneDialogResult(bool Containers, bool Images, bool Volumes);
