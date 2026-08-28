// SPDX-License-Identifier: EUPL-1.2
using System.Text;

namespace Aetheus.Back.Components.Analysis;

internal static class AnalysisPolicyKey
{
    private const int MaxLength = 200;

    public static string Resolve(string? requestedKey, string name)
    {
        if (!string.IsNullOrWhiteSpace(requestedKey))
            return NormalizeRequested(requestedKey);

        var builder = new StringBuilder(Math.Min(name.Length, MaxLength));
        var separatorPending = false;
        foreach (var value in name)
        {
            var character = char.ToLowerInvariant(value);
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (separatorPending && builder.Length > 0 && builder.Length < MaxLength)
                    builder.Append('.');
                if (builder.Length < MaxLength)
                    builder.Append(character);
                separatorPending = false;
                continue;
            }

            separatorPending = builder.Length > 0;
        }

        var key = builder.ToString().TrimEnd('.');
        if (key.Length == 0)
            throw new BadRequestException("The policy name must contain at least one ASCII letter or digit.");
        return key;
    }

    public static string ForExisting(Data.Entities.AnalysisPolicy policy) =>
        string.IsNullOrWhiteSpace(policy.PolicyKey) ? $"legacy.{policy.Id}" : policy.PolicyKey;

    private static string NormalizeRequested(string requestedKey)
    {
        var key = requestedKey.Trim().ToLowerInvariant();
        if (key.Length > MaxLength
            || key[0] is '.' or '-'
            || key[^1] is '.' or '-'
            || key.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-'))
            || key.Contains("..", StringComparison.Ordinal))
            throw new BadRequestException(
                "Policy keys must contain lowercase ASCII letters, digits, dots or hyphens.");
        return key;
    }
}
