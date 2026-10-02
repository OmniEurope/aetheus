// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Users;

public partial class AdminIdentityNav : IDisposable
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private AdminIdentitySearch Search { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Parameter, EditorRequired] public string ActiveSection { get; set; } = string.Empty;

    protected override void OnInitialized() => Search.Changed += OnSearchChanged;

    private Task OnSearchChanged() => InvokeAsync(StateHasChanged);

    private async Task OnTermChangedAsync(string? term)
    {
        var section = await Search.SetAsync(term, ActiveSection);
        if (!section.Equals(ActiveSection, StringComparison.Ordinal))
            Nav.NavigateTo($"/admin/{section}");
    }

    private string LinkText(string label, string section) =>
        Search.CountFor(section) is > 0 and var count
            ? $"{label} ({count.ToString(System.Globalization.CultureInfo.CurrentCulture)})"
            : label;

    private string LinkClass(string section) =>
        ActiveSection.Equals(section, StringComparison.Ordinal)
            ? "iam-section-link iam-section-link-active"
            : "iam-section-link";

    private string? AriaCurrent(string section) =>
        ActiveSection.Equals(section, StringComparison.Ordinal) ? "page" : null;

    public void Dispose() => Search.Changed -= OnSearchChanged;
}
