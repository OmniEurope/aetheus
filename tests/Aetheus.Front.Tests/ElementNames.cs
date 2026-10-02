// SPDX-License-Identifier: EUPL-1.2

using AngleSharp.Dom;

namespace Aetheus.Front.Tests;

/// <summary>
/// PLAN-003 lot 4 / D9: a button inside a data row shows only its icon, and carries its name in
/// <c>title</c> and <c>aria-label</c>. Tests that used to find a row action by its visible text now
/// look at everything that names it, so they keep testing the same action rather than the same markup.
/// </summary>
internal static class ElementNames
{
    /// <summary>Visible text plus the accessible names of the element, joined for a substring search.</summary>
    internal static string Names(this IElement element) =>
        $"{element.TextContent} {element.GetAttribute("title")} {element.GetAttribute("aria-label")}";
}
