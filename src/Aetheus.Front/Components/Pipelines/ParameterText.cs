// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;

namespace Aetheus.Front.Components.Pipelines;

/// <summary>
/// PLAN-003 D41: a pipeline parameter may carry its label and help in French beside the default
/// (<c>display_name_fr</c>, <c>description_fr</c>). The page shows the French text when the interface
/// is in French and the pipeline wrote one; otherwise the default, so a YAML written before this
/// reads exactly as it did.
/// </summary>
internal static class ParameterText
{
    public static string? Pick(string? text, string? french) =>
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(french)
            ? french
            : text;
}
