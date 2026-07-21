// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// P (queue-time run parameters): validates user-supplied parameter values against a pipeline's
/// declared <c>parameters:</c> block and computes the effective value (supplied value, else the
/// declared default) for each parameter. Type/required/allowed-values are enforced here so an
/// invalid launch is rejected before a run is created.
/// </summary>
public static class PipelineParameterResolver
{
    /// <summary>
    /// Validates <paramref name="supplied"/> against <paramref name="declared"/>. On success returns
    /// the effective name→value map (only declared parameters that resolve to a non-null value).
    /// </summary>
    public static bool TryResolve(
        IReadOnlyList<PipelineTemplateParameterDefinition> declared,
        IReadOnlyDictionary<string, string>? supplied,
        out Dictionary<string, string> effective,
        out List<string> errors)
    {
        effective = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        errors = [];
        supplied ??= new Dictionary<string, string>();

        var declaredNames = declared
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Reject inputs that don't correspond to a declared parameter (typo / stale dialog).
        foreach (var key in supplied.Keys)
        {
            if (!declaredNames.Contains(key))
                errors.Add($"Unknown parameter '{key}'.");
        }

        foreach (var param in declared)
        {
            if (string.IsNullOrWhiteSpace(param.Name)) continue;
            if (IsReservedName(param.Name))
            {
                errors.Add($"Parameter '{param.Name}' uses a reserved system variable name.");
                continue;
            }

            var hasSupplied = supplied.TryGetValue(param.Name, out var rawValue) && !string.IsNullOrWhiteSpace(rawValue);
            var value = hasSupplied ? rawValue! : param.Default;

            if (string.IsNullOrWhiteSpace(value))
            {
                if (param.Required)
                    errors.Add($"Parameter '{param.Name}' is required.");
                continue; // optional + no value → not injected
            }

            var type = (param.Type ?? "string").Trim().ToLowerInvariant();
            switch (type)
            {
                case "number":
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                        errors.Add($"Parameter '{param.Name}' must be a number (got '{value}').");
                    break;
                case "boolean":
                    if (!bool.TryParse(value, out _))
                        errors.Add($"Parameter '{param.Name}' must be 'true' or 'false' (got '{value}').");
                    break;
                case "choice":
                    if (param.AllowedValues.Count > 0 &&
                        !param.AllowedValues.Any(v => string.Equals(v, value, StringComparison.Ordinal)))
                        errors.Add($"Parameter '{param.Name}' must be one of: {string.Join(", ", param.AllowedValues)} (got '{value}').");
                    break;
            }

            effective[param.Name] = value;
        }

        return errors.Count == 0;
    }

    internal static bool IsReservedName(string name)
        => name.StartsWith("AETHEUS_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("BUILD_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("SYSTEM_", StringComparison.OrdinalIgnoreCase);

    /// <summary>The declared defaults (name→default) for parameters that declare one - injected at the
    /// lowest precedence so an explicit YAML <c>variables:</c> entry of the same name still wins.</summary>
    public static Dictionary<string, string> Defaults(IReadOnlyList<PipelineTemplateParameterDefinition> declared)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in declared)
        {
            if (!string.IsNullOrWhiteSpace(p.Name)
                && !IsReservedName(p.Name)
                && !string.IsNullOrWhiteSpace(p.Default))
                map[p.Name] = p.Default!;
        }
        return map;
    }
}
