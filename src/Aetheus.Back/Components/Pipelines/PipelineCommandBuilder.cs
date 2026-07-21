// SPDX-License-Identifier: EUPL-1.2
using System.Text;
using System.Text.RegularExpressions;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Pipelines;

/// <summary>
/// Shell command construction for pipeline steps and system tasks (clone / prepare / cleanup).
/// Repo URL / branch / credentials are passed via environment variables so user-controlled values
/// cannot break out of the shell template (defense in depth against command injection).
/// Git auth uses an ephemeral <c>http.extraHeader</c> injected through <c>GIT_CONFIG_*</c>
/// environment variables (F-013): credentials never appear in the remote URL, are never persisted
/// to the workspace <c>.git/config</c>, and never show up in the process argument list.
/// </summary>
internal static partial class PipelineCommandBuilder
{
    // F-033: single home for the magic workspace paths and system-task timeouts.
    internal const int PrepareTimeoutSeconds = 120;
    internal const int CleanupTimeoutSeconds = 60;
    internal const int ArtifactCollectionTimeoutSeconds = 300;

    // Workspace lives under the OS temp dir so the agent's systemd sandbox (PrivateTmp=true,
    // ProtectSystem=strict) keeps it writable AND isolated from the host /tmp. The slot is an
    // opaque hash of the run id (Azure-style short path) - neither the app name nor the
    // sequential run id leaks into logs or `pwd`.
    internal static string GetDefaultWorkspace(int runId, bool isWindows)
    {
        var slot = GetWorkspaceSlot(runId);
        return isWindows ? $@"C:\w\{slot}\s" : $"/tmp/{slot}/s";
    }

    internal static string GetWorkspaceSlot(int runId)
    {
        var mixed = (uint)runId * 2654435761u;
        return mixed.ToString("x8");
    }

    [GeneratedRegex(@"\$\(([A-Za-z_][A-Za-z0-9_]*)\)")]
    private static partial Regex VariablePattern();

    // S-TECH-V6QN: ${{ parameters.X }} is a distinct namespace from $(X). It resolves only from the
    // run-parameter keys (carried as "parameters.X" in the variable map), so a same-named YAML
    // variable can never shadow a run parameter - matching Azure DevOps' compile-time-vs-runtime split.
    [GeneratedRegex(@"\$\{\{\s*parameters\.([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex ParameterPattern();

    /// <summary>Prefix under which run parameters are stored in the variable map for the
    /// <c>${{ parameters.X }}</c> namespace. Dotted, so it is never a valid shell env name and is
    /// stripped from the exported step environment (see <c>ScopeStepEnvironment</c>).</summary>
    internal const string ParameterKeyPrefix = "parameters.";

    internal static string Substitute(string command, Dictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(command) || variables.Count == 0)
            return command;

        // Resolve the ${{ parameters.X }} namespace first, then the $(X) variable syntax.
        command = ParameterPattern().Replace(command, match =>
        {
            var paramName = match.Groups[1].Value;
            return variables.TryGetValue(ParameterKeyPrefix + paramName, out var pv) ? pv : match.Value;
        });

        return VariablePattern().Replace(command, match =>
        {
            var varName = match.Groups[1].Value;
            return variables.TryGetValue(varName, out var value) ? value : match.Value;
        });
    }

    // --- Git auth preambles (F-013) ---

    private const string LinuxGitAuthPreamble =
        "if [ -n \"${GIT_USERNAME:-}\" ] && [ -n \"${GIT_PASSWORD:-}\" ]; then\n" +
        "  export GIT_CONFIG_COUNT=1\n" +
        "  export GIT_CONFIG_KEY_0=\"http.extraHeader\"\n" +
        "  export GIT_CONFIG_VALUE_0=\"Authorization: Basic $(printf '%s:%s' \"${GIT_USERNAME}\" \"${GIT_PASSWORD}\" | base64 | tr -d '\\n')\"\n" +
        "fi\n";

    private const string WindowsGitAuthPreamble =
        "if ($env:GIT_USERNAME -and $env:GIT_PASSWORD) {\n" +
        "  $__pair = \"$($env:GIT_USERNAME):$($env:GIT_PASSWORD)\"\n" +
        "  $env:GIT_CONFIG_COUNT = '1'\n" +
        "  $env:GIT_CONFIG_KEY_0 = 'http.extraHeader'\n" +
        "  $env:GIT_CONFIG_VALUE_0 = 'Authorization: Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($__pair))\n" +
        "}\n";

    // --- Clone scripts (F-016: single builder, parameterized by the working-dir expression) ---

    private static string BuildLinuxCloneScript(string workdirExpr) =>
        "set -e\n" +
        "export GIT_TERMINAL_PROMPT=0\n" +
        LinuxGitAuthPreamble +
        $"mkdir -p \"{workdirExpr}\"\n" +
        $"chmod 700 \"{workdirExpr}\"\n" +
        $"cd \"{workdirExpr}\"\n" +
        "GIT_HISTORY_DEPTH=\"${AETHEUS_GIT_HISTORY_DEPTH:-1}\"\n" +
        "case \"$GIT_HISTORY_DEPTH\" in *[!0-9]*|'') echo \"AETHEUS_GIT_HISTORY_DEPTH must be a positive integer.\" >&2; exit 1 ;; esac\n" +
        "[ \"$GIT_HISTORY_DEPTH\" -ge 1 ] || { echo \"AETHEUS_GIT_HISTORY_DEPTH must be at least 1.\" >&2; exit 1; }\n" +
        "if [ -n \"${BUILD_SOURCEVERSION:-}\" ]; then\n" +
        "  if [ ! -d .git ]; then git init; git remote add origin \"${REPOSITORY_URL}\"; fi\n" +
        "  git fetch --depth \"$GIT_HISTORY_DEPTH\" origin \"${DEFAULT_BRANCH:-main}\"\n" +
        "  if ! git cat-file -e \"${BUILD_SOURCEVERSION}^{commit}\" 2>/dev/null; then\n" +
        "    git fetch --depth 1 origin \"${BUILD_SOURCEVERSION}\" || true\n" +
        "  fi\n" +
        "  if ! git cat-file -e \"${BUILD_SOURCEVERSION}^{commit}\" 2>/dev/null; then\n" +
        "    git fetch --depth 1 origin '+refs/heads/*:refs/remotes/origin/*'\n" +
        "  fi\n" +
        "  if ! git cat-file -e \"${BUILD_SOURCEVERSION}^{commit}\" 2>/dev/null; then\n" +
        "    git fetch --deepen 1000 origin '+refs/heads/*:refs/remotes/origin/*'\n" +
        "  fi\n" +
        "  git cat-file -e \"${BUILD_SOURCEVERSION}^{commit}\"\n" +
        "  git checkout --detach \"${BUILD_SOURCEVERSION}\"\n" +
        "  git reset --hard \"${BUILD_SOURCEVERSION}\"\n" +
        "  echo \"Repository pinned to ${BUILD_SOURCEVERSION}\"\n" +
        "elif [ -d .git ]; then\n" +
        "  git fetch --depth 1 origin \"${DEFAULT_BRANCH:-main}\"\n" +
        "  git reset --hard \"origin/${DEFAULT_BRANCH:-main}\"\n" +
        "  echo \"Repository updated (fetch + reset)\"\n" +
        "else\n" +
        "  git clone --branch \"${DEFAULT_BRANCH:-main}\" --single-branch --depth 1 \"${REPOSITORY_URL}\" .\n" +
        "  echo \"Repository cloned\"\n" +
        "fi\n";

    private static string BuildWindowsCloneScript(string workdirExpr) =>
        "$ErrorActionPreference = 'Stop'\n" +
        "$env:GIT_TERMINAL_PROMPT = '0'\n" +
        WindowsGitAuthPreamble +
        $"New-Item -ItemType Directory -Force -Path '{workdirExpr}' | Out-Null\n" +
        $"Set-Location '{workdirExpr}'\n" +
        "$branch = if ($env:DEFAULT_BRANCH) { $env:DEFAULT_BRANCH } else { 'main' }\n" +
        "$historyDepth = if ($env:AETHEUS_GIT_HISTORY_DEPTH) { $env:AETHEUS_GIT_HISTORY_DEPTH } else { '1' }\n" +
        "if ($historyDepth -notmatch '^[1-9][0-9]*$') { throw 'AETHEUS_GIT_HISTORY_DEPTH must be a positive integer.' }\n" +
        "if ($env:BUILD_SOURCEVERSION) {\n" +
        "  if (-not (Test-Path '.git')) { git init; git remote add origin $env:REPOSITORY_URL }\n" +
        "  git fetch --depth $historyDepth origin $branch\n" +
        "  git cat-file -e \"$env:BUILD_SOURCEVERSION`^{commit}\" 2>$null\n" +
        "  if ($LASTEXITCODE -ne 0) { git fetch --depth 1 origin $env:BUILD_SOURCEVERSION }\n" +
        "  git cat-file -e \"$env:BUILD_SOURCEVERSION`^{commit}\" 2>$null\n" +
        "  if ($LASTEXITCODE -ne 0) { git fetch --depth 1 origin '+refs/heads/*:refs/remotes/origin/*' }\n" +
        "  git cat-file -e \"$env:BUILD_SOURCEVERSION`^{commit}\" 2>$null\n" +
        "  if ($LASTEXITCODE -ne 0) { git fetch --deepen 1000 origin '+refs/heads/*:refs/remotes/origin/*' }\n" +
        "  git cat-file -e \"$env:BUILD_SOURCEVERSION`^{commit}\"\n" +
        "  if ($LASTEXITCODE -ne 0) { throw 'Pinned source commit is not reachable from the configured branch.' }\n" +
        "  git checkout --detach $env:BUILD_SOURCEVERSION\n" +
        "  git reset --hard $env:BUILD_SOURCEVERSION\n" +
        "  Write-Output \"Repository pinned to $env:BUILD_SOURCEVERSION\"\n" +
        "} elseif (Test-Path '.git') {\n" +
        "  git fetch --depth 1 origin $branch\n" +
        "  git reset --hard \"origin/$branch\"\n" +
        "  Write-Output 'Repository updated (fetch + reset)'\n" +
        "} else {\n" +
        "  git clone --branch $branch --single-branch --depth 1 $env:REPOSITORY_URL .\n" +
        "  Write-Output 'Repository cloned'\n" +
        "}\n";

    internal static string BuildLinuxCloneCommand(string workspace) =>
        BuildLinuxCloneScript(workspace) +
        "echo \"Workspace ready: ${PWD}\"\n" +
        "ls -la";

    internal static string BuildWindowsCloneCommand(string workspace) =>
        BuildWindowsCloneScript(workspace) +
        "Write-Output \"Workspace ready: $(Get-Location)\"\n" +
        "Get-ChildItem | Format-Table Name, Length, LastWriteTime -AutoSize";

    internal static string BuildLinuxPrepareCommand(string workspace) =>
        "set -e\n" +
        $"mkdir -p \"{workspace}\"\n" +
        $"chmod 700 \"{workspace}\"\n" +
        $"echo \"Workspace prepared: {workspace}\"\n" +
        $"ls -la \"{workspace}\"";

    internal static string BuildWindowsPrepareCommand(string workspace) =>
        "$ErrorActionPreference = 'Stop'\n" +
        $"New-Item -ItemType Directory -Force -Path '{workspace}' | Out-Null\n" +
        $"Write-Output 'Workspace prepared: {workspace}'\n" +
        $"Get-ChildItem '{workspace}' | Format-Table Name, Length -AutoSize";

    internal static string BuildLinuxCleanupCommand(string workspace) =>
        "set +e\n" +
        $"echo \"Cleaning up workspace: {workspace}\"\n" +
        $"if [ -d \"{workspace}\" ]; then\n" +
        $"  rm -rf \"{workspace}\"\n" +
        "  echo \"Workspace cleaned successfully\"\n" +
        "else\n" +
        "  echo \"Workspace already clean\"\n" +
        "fi\n" +
        "echo \"Cleanup complete\"";

    internal static string BuildWindowsCleanupCommand(string workspace) =>
        $"Write-Output 'Cleaning up workspace: {workspace}'\n" +
        $"if (Test-Path '{workspace}') {{\n" +
        $"  Remove-Item -Recurse -Force '{workspace}'\n" +
        "  Write-Output 'Workspace cleaned successfully'\n" +
        "} else {\n" +
        "  Write-Output 'Workspace already clean'\n" +
        "}\n" +
        "Write-Output 'Cleanup complete'";

    /// <summary>
    /// F-032 / S-TECH-52: rewrites bash-style <c>&amp;&amp;</c> chains into PowerShell exit-code
    /// checks, skipping occurrences inside single- or double-quoted literals. The scanner mirrors
    /// PowerShell's own quoting rules so an escaped quote never desyncs the string state:
    /// <list type="bullet">
    /// <item>a backtick (<c>`</c>) escapes the next character outside single-quoted literals
    /// (including inside double-quoted strings), so <c>`"</c> is a literal quote, not a terminator;</item>
    /// <item>a doubled quote (<c>''</c> inside single, <c>""</c> inside double) is a literal quote, not
    /// a close-then-reopen.</item>
    /// </list>
    /// Without these rules a <c>&amp;&amp;</c> embedded in a string containing an escaped quote was
    /// rewritten as if it were a top-level operator, corrupting the command.
    /// </summary>
    internal static string RewritePwshAndChains(string command)
    {
        if (!command.Contains("&&", StringComparison.Ordinal)) return command;

        var sb = new StringBuilder(command.Length + 64);
        bool inSingle = false, inDouble = false;
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            // Backtick escape (PowerShell): outside single-quoted literals it escapes the next char,
            // so emit both verbatim and never let an escaped quote flip the string state.
            if (c == '`' && !inSingle && i + 1 < command.Length)
            {
                sb.Append(c).Append(command[i + 1]);
                i++;
                continue;
            }

            if (c == '\'' && !inDouble)
            {
                // Doubled '' inside a single-quoted string is a literal quote, not a terminator.
                if (inSingle && i + 1 < command.Length && command[i + 1] == '\'')
                {
                    sb.Append("''");
                    i++;
                    continue;
                }
                inSingle = !inSingle;
                sb.Append(c);
                continue;
            }

            if (c == '"' && !inSingle)
            {
                // Doubled "" inside a double-quoted string is a literal quote, not a terminator.
                if (inDouble && i + 1 < command.Length && command[i + 1] == '"')
                {
                    sb.Append("\"\"");
                    i++;
                    continue;
                }
                inDouble = !inDouble;
                sb.Append(c);
                continue;
            }

            if (c == '&' && !inSingle && !inDouble && i + 1 < command.Length && command[i + 1] == '&')
            {
                sb.Append("; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE };");
                i++;
                continue;
            }

            sb.Append(c);
        }
        return sb.ToString();
    }

    // Phase 3: reject a working_directory that escapes the pipeline workspace (absolute path outside
    // it, or any parent-dir traversal). Keeps a step's filesystem footprint inside the sandbox root.
    internal static bool WorkingDirectoryEscapesWorkspace(string? workDir, string? workspace)
    {
        if (string.IsNullOrEmpty(workDir)) return false;
        var workSegments = workDir.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (workSegments.Contains("..", StringComparer.Ordinal)) return true;
        var isAbsolute = workDir.StartsWith('/') || workDir.StartsWith('\\')
            || (workDir.Length >= 2 && char.IsLetter(workDir[0]) && workDir[1] == ':');
        if (!isAbsolute) return false; // relative paths resolve under the workspace - allowed
        if (string.IsNullOrEmpty(workspace)) return true; // absolute with no known workspace - unsafe

        var workspaceSegments = workspace.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (workspaceSegments.Contains("..", StringComparer.Ordinal)) return true;
        var windowsStyle = workDir.Contains('\\', StringComparison.Ordinal)
            || workspace.Contains('\\', StringComparison.Ordinal)
            || (workDir.Length >= 2 && workDir[1] == ':')
            || (workspace.Length >= 2 && workspace[1] == ':');
        var comparison = windowsStyle ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedWorkDir = string.Join('/', workSegments.Where(s => s != "."));
        var normalizedWorkspace = string.Join('/', workspaceSegments.Where(s => s != "."));

        return !normalizedWorkDir.Equals(normalizedWorkspace, comparison)
            && !normalizedWorkDir.StartsWith(normalizedWorkspace + "/", comparison);
    }

    // P-07: Build every step inside its run workspace. An explicit working_directory may narrow
    // that location, while checkout only controls source preparation (handled by System:Prepare).
    internal static string BuildStepCommand(PipelineStepDefinition stepDef, Dictionary<string, string> stageVars, bool isWindows)
    {
        var vars = new Dictionary<string, string>(stageVars, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(stepDef.WorkingDirectory))
        {
            var resolvedWorkDir = Substitute(stepDef.WorkingDirectory, vars);
            if (WorkingDirectoryEscapesWorkspace(resolvedWorkDir, vars.GetValueOrDefault("WORKSPACE")))
                return isWindows
                    ? $"$ErrorActionPreference='Stop'\nWrite-Error \"working_directory '{resolvedWorkDir}' is outside the pipeline workspace - blocked.\"\nexit 1"
                    : $"echo \"working_directory '{resolvedWorkDir}' is outside the pipeline workspace - blocked.\" >&2\nexit 1";
        }

        var command = Substitute(stepDef.Shell, vars);
        if (isWindows)
            command = RewritePwshAndChains(command);

        string? workDir = null;
        if (!string.IsNullOrEmpty(stepDef.WorkingDirectory))
            workDir = Substitute(stepDef.WorkingDirectory, vars);
        else if (vars.TryGetValue("WORKSPACE", out var workspace) && !string.IsNullOrWhiteSpace(workspace))
            workDir = workspace;

        if (!string.IsNullOrEmpty(workDir))
        {
            // 0-d (double-clone fix): `checkout: true` no longer re-clones the repo. The auto-injected
            // system Prepare step already cloned REPOSITORY_URL into the run workspace before any stage
            // runs (every repo-backed run gets one, pinned to the same affinity server), so re-running
            // the clone preamble here cloned the repo a SECOND time - a redundant fetch + reset over the
            // network with no benefit. Every step now enters the run workspace, including pipelines
            // without a repository, so concurrent runs never share the agent's global work directory.
            command = isWindows
                ? $"Set-Location '{workDir}'\n{command}"
                : $"cd \"{workDir}\"\n{command}";
        }

        if (isWindows)
        {
            if (!command.Contains("$ErrorActionPreference", StringComparison.Ordinal))
                command = $"$ErrorActionPreference = 'Stop'\n{command}";
        }
        else
        {
            if (!command.StartsWith("set -e", StringComparison.Ordinal))
                command = $"set -e\n{command}";
        }

        return command;
    }
}
