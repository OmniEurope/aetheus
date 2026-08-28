// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aetheus.Back.Components.Pipelines;

public static partial class PipelineConditionEvidence
{
    internal const int MaxConditionLength = 1_000;
    internal const int MaxVariablesJsonLength = 4_000;
    private const int MaxCapturedValueLength = 512;
    private const string TruncatedKey = "aetheus:evidence:truncated";
    private const string HashKey = "aetheus:evidence:sha256";

    [GeneratedRegex(@"variables\['(\w+)'\]", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionVariablePattern();

    /// <summary>
    /// Captures the variables a condition read, for the evidence shown next to a skipped step.
    ///
    /// Two redactions, not one. <paramref name="secretKeys"/> covers values declared in the pipeline
    /// vault, matched BY NAME. <paramref name="runtimeSecretValues"/> covers secrets minted during the
    /// run (the deployment bootstrap password, the ephemeral Git token) which carry no vault name at
    /// all: they are matched BY VALUE, because a condition naming them would otherwise persist a live
    /// credential in clear and serve it unmasked forever after.
    /// </summary>
    public static Dictionary<string, string> CaptureVariables(
        string condition,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlySet<string> secretKeys,
        IReadOnlyCollection<string>? runtimeSecretValues = null)
    {
        var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ConditionVariablePattern().Matches(condition))
        {
            var name = match.Groups[1].Value;
            if (secretKeys.Contains(name))
            {
                captured[name] = "[masked]";
                continue;
            }

            var value = variables.GetValueOrDefault(name, "[not set]");
            captured[name] = IsRuntimeSecret(value, runtimeSecretValues) ? "[masked]" : value;
        }

        return captured;
    }

    private static bool IsRuntimeSecret(string value, IReadOnlyCollection<string>? runtimeSecretValues) =>
        runtimeSecretValues is { Count: > 0 }
        && !string.IsNullOrEmpty(value)
        && runtimeSecretValues.Contains(value, StringComparer.Ordinal);

    public static string CompactCondition(string condition) =>
        CompactText(condition, MaxConditionLength);

    public static string? SerializeVariables(IReadOnlyDictionary<string, string> variables)
    {
        if (variables.Count == 0) return null;

        var serialized = JsonSerializer.Serialize(variables);
        if (serialized.Length <= MaxVariablesJsonLength) return serialized;

        var compacted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TruncatedKey] = "true",
            [HashKey] = ComputeHash(serialized)
        };
        foreach (var (key, value) in variables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            compacted[key] = CompactText(value, MaxCapturedValueLength);
            var candidate = JsonSerializer.Serialize(compacted);
            if (candidate.Length <= MaxVariablesJsonLength) continue;

            compacted.Remove(key);
            break;
        }

        return JsonSerializer.Serialize(compacted);
    }

    private static string CompactText(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        var suffix = $"… [sha256:{ComputeHash(value)}]";
        return string.Concat(value.AsSpan(0, maxLength - suffix.Length), suffix);
    }

    private static string ComputeHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
