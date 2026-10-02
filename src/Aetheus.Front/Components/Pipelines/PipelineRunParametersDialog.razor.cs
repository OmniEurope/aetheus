// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

public partial class PipelineRunParametersDialog
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter] public Dictionary<string, string> Parameters { get; set; } = [];

    private List<KeyValuePair<string, string>> ParameterList =>
        Parameters.OrderBy(parameter => parameter.Key).ToList();
}
