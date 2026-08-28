// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class DockerResourceLimitsDialog
{
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public string ContainerId { get; set; } = string.Empty;
    [Parameter] public double CpuLimit { get; set; }
    [Parameter] public int MemoryLimitMb { get; set; }

    private DockerResourceLimitsDialogModel _model = new();

    protected override void OnParametersSet() => _model = new DockerResourceLimitsDialogModel { CpuLimit = CpuLimit, MemoryLimitMb = MemoryLimitMb };
    private void Submit(DockerResourceLimitsDialogModel model) => Dialog.Close(new DockerResourceLimitsDialogResult(model.CpuLimit, model.MemoryLimitMb));
}

public sealed class DockerResourceLimitsDialogModel
{
    public double CpuLimit { get; set; }
    public int MemoryLimitMb { get; set; }
}

public sealed record DockerResourceLimitsDialogResult(double CpuLimit, int MemoryLimitMb);
