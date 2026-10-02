// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.RegularExpressions;

namespace Aetheus.Shared.Components.Pipelines;

/// <summary>
/// Keeps pipeline secret values out of persisted command text. The backend stores an opaque
/// placeholder and the agent resolves it from the already-protected task environment immediately
/// before execution.
/// </summary>
public static partial class PipelineSecretPlaceholder
{
    private const string Prefix = "__AETHEUS_SECRET_";

    [GeneratedRegex("__AETHEUS_SECRET_([A-Za-z0-9_-]+)__", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    public static string Create(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{Prefix}{encoded}__";
    }

    public static string Resolve(string command, IReadOnlyDictionary<string, string> environment)
    {
        if (string.IsNullOrEmpty(command) || environment.Count == 0)
            return command;

        return PlaceholderPattern().Replace(command, match =>
        {
            var encoded = match.Groups[1].Value.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');

            try
            {
                var key = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                return environment.TryGetValue(key, out var value) ? value : match.Value;
            }
            catch (FormatException)
            {
                return match.Value;
            }
        });
    }
}
