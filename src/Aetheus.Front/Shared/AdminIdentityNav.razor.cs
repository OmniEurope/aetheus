// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Shared;

public partial class AdminIdentityNav
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public string ActiveSection { get; set; } = string.Empty;

    private string LinkClass(string section) =>
        ActiveSection.Equals(section, StringComparison.Ordinal)
            ? "iam-section-link iam-section-link-active"
            : "iam-section-link";

    private string? AriaCurrent(string section) =>
        ActiveSection.Equals(section, StringComparison.Ordinal) ? "page" : null;
}
