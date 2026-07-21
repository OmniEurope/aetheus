// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Agent.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Executors;

public interface ICommandValidator
{
    bool IsAllowed(string command);
    string? GetRejectionReason(string command);
    bool HasDangerousEnvironmentVariables(Dictionary<string, string> environmentVariables);
}

// SECURITY MODEL: this allow-list is a defense-in-depth GUARDRAIL that limits the blast radius
// of a pipeline/task command - it is NOT a sandbox and NOT the authorization boundary. A
// determined author can still reach arbitrary code via allow-listed interpreters (python/node/
// dotnet), package managers, or git/tar living-off-the-land. The real boundary is RBAC: free-form
// shell requires Server Admin on the target (F-15 on POST /api/tasks; F-EXEC-1 on the pipeline
// path). Do not rely on IsAllowed as a security control.
public sealed partial class CommandValidator(IOptions<AetheusAgentOptions> options) : ICommandValidator
{
    private static readonly HashSet<string> DangerousEnvVars = new(StringComparer.OrdinalIgnoreCase)
    {
        "LD_PRELOAD", "LD_LIBRARY_PATH", "LD_AUDIT", "GCONV_PATH", "BASH_ENV", "ENV",
        "PYTHONSTARTUP", "PERL5OPT", "RUBYOPT", "NODE_OPTIONS",
        "IFS", "SHELL", "HISTFILE",
        "PATH", "HOME", "USER", "LOGNAME", "CDPATH",
        "PROMPT_COMMAND", "SHELLOPTS", "BASHOPTS", "GLOBIGNORE",
        "DOTNET_STARTUP_HOOKS", "CORECLR_PROFILER", "CORECLR_PROFILER_PATH", "CORECLR_ENABLE_PROFILING",
        "DOCKER_HOST", "DOCKER_CONTEXT", "GIT_CONFIG_GLOBAL",
        "DYLD_INSERT_LIBRARIES", "DYLD_LIBRARY_PATH",
        "PSModulePath", "PSModuleAutoloadingPreference", "COMSPEC", "PATHEXT", "PYTHONPATH"
    };

    private readonly List<Regex> _allowedPatterns = options.Value.AllowedCommandPatterns
        .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)))
        .ToList();

    // A command is either a single allow-listed invocation or several joined by `&&`
    // (sequential AND - the only chaining operator permitted, so multi-step provisioning
    // tasks can be expressed). Every segment must INDEPENDENTLY be metacharacter-free and
    // match an allow-list pattern, so the blast radius of a chain is just the union of
    // individually-allowed commands. Every other shell metacharacter (`;`, `|`, a lone
    // `&`, `$()`, backticks, redirections) stays blocked inside every segment.
    // Single point of truth: IsAllowed and GetRejectionReason previously diverged (the
    // multi-line short-circuit lived only in IsAllowed), so a caller relying on
    // "GetRejectionReason == null" applied a different policy than IsAllowed.
    public bool IsAllowed(string command) => GetRejectionReason(command) is null;

    public string? GetRejectionReason(string command)
    {
        var normalized = command.Trim();
        if (string.IsNullOrEmpty(normalized))
            return "Empty command";

        // Pipeline scripts are multi-line bash passed via `bash -c`. They contain newlines,
        // semicolons, and other metacharacters by design. The real authorization boundary is
        // RBAC (F-EXEC-1: Server.Admin required to trigger a pipeline), not this allow-list.
        // Multi-line scripts are recognized by the presence of a newline - single-line task
        // commands (the original use case) continue to be validated segment-by-segment.
        if (normalized.Contains('\n'))
            return null;

        foreach (var segment in normalized.Split("&&"))
        {
            var trimmed = segment.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return "Empty segment in chain";
            if (ShellMetaCharsRegex().IsMatch(trimmed))
                return $"Blocked metacharacter in segment: {Truncate(trimmed)}";
            if (!_allowedPatterns.Any(p => p.IsMatch(trimmed)))
                return $"No allow-list pattern matched segment: {Truncate(trimmed)}";
        }
        return null;
    }

    private static string Truncate(string s) => s.Length <= 80 ? s : s[..80] + "…";

    [GeneratedRegex(@"[;|&`$(){}\<>\r\n\0]")]
    private static partial Regex ShellMetaCharsRegex();

    public bool HasDangerousEnvironmentVariables(Dictionary<string, string> environmentVariables)
    {
        foreach (var (key, value) in environmentVariables)
        {
            if (DangerousEnvVars.Contains(key))
                return true;

            // Shellshock (CVE-2014-6271 / -7169) class: bash <4.3 imports an env var whose value
            // starts with "() {" as a function (executing trailing bytes); bash >=4.3 namespaces
            // them as BASH_FUNC_x%%. Block both forms regardless of the host's bash version.
            if (key.StartsWith("BASH_FUNC_", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value is not null && value.TrimStart().StartsWith("() {", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
