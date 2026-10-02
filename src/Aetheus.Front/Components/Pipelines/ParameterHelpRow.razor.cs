// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// A parameter's label or field with its help icon. The description lives in the icon's tooltip
/// (hover and keyboard focus) and in a visually hidden copy that the field names through
/// <c>aria-describedby</c>, using <see cref="DescriptionId"/>. Shared by the run parameters and the
/// template parameters of a new pipeline so both open the same tooltip the same way.
/// </summary>
public partial class ParameterHelpRow
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>The parameter's description; no icon is drawn without one.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>The id of the hidden description, to put in the field's <c>aria-describedby</c>.</summary>
    [Parameter, EditorRequired] public string DescriptionId { get; set; } = string.Empty;

    /// <summary>The <see cref="DescriptionId"/> of a parameter, or <c>null</c> when it has no description.</summary>
    public static string? DescribedBy(string scope, string parameterName, string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : $"{scope}-desc-{parameterName}";

}
