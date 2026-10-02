// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.ServerDetailSections;

public partial class AddModuleLinkDialog
{
    [Parameter] public ModuleLinkType[] TargetTypes { get; set; } = [];
    [Parameter] public List<string> Resources { get; set; } = [];

    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private ModuleLinkType _targetType;
    private string _targetIdentifier = string.Empty;
    private string _sourceIdentifier = string.Empty;

    protected override void OnInitialized()
    {
        _targetType = TargetTypes.Length > 0 ? TargetTypes[0] : default;
        _sourceIdentifier = Resources.Count > 0 ? Resources[0] : string.Empty;
    }

    private void Save() => Dialog.Close(new AddModuleLinkResult(_targetType, _targetIdentifier, _sourceIdentifier));
}

public sealed record AddModuleLinkResult(ModuleLinkType TargetType, string TargetIdentifier, string SourceIdentifier);
