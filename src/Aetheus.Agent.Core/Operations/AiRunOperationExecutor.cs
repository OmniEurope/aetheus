// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace Aetheus.Agent.Core.Operations;

public sealed class AiRunOperationExecutor(
    IServerApiClient apiClient,
    IShellRunner shellRunner,
    IOptions<AetheusAgentOptions> options,
    ILogger<AiRunOperationExecutor> logger) : IOperationExecutor
{
    private readonly AetheusAgentOptions _options = options.Value;

    public bool CanHandle(OperationKind kind) => kind == OperationKind.AiRun;

    public Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            kind, target, new Dictionary<string, string>(), timeoutSeconds, onOutput, cancellationToken);

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind,
        string target,
        IReadOnlyDictionary<string, string> envVars,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        if (kind != OperationKind.AiRun || !OperationTargetValidator.IsValid(kind, target))
            return new ExecutorResult(-1, false);
        if (!TryReadConfiguration(envVars, out var configuration, out var error))
        {
            await onOutput(error, TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var tempRoot = Path.Combine(
            _options.WorkDirectory, "ai-temp", Guid.NewGuid().ToString("N"));
        var ownsWorkspace = string.IsNullOrWhiteSpace(configuration.WorkingDirectory);
        var workingDirectory = ownsWorkspace
            ? Path.Combine(tempRoot, "workspace")
            : Path.GetFullPath(configuration.WorkingDirectory);
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(workingDirectory);
        var promptPath = Path.Combine(tempRoot, "prompt.md");
        var outputPath = Path.Combine(tempRoot, "report.md");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            return await ExecuteConfiguredAsync(
                configuration, ownsWorkspace, workingDirectory, promptPath, outputPath,
                timeoutSeconds, stopwatch, onOutput, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CleanupTemporaryDirectory(tempRoot);
        }
    }

    private async Task<ExecutorResult> ExecuteConfiguredAsync(
        AiRunConfiguration configuration,
        bool ownsWorkspace,
        string workingDirectory,
        string promptPath,
        string outputPath,
        int timeoutSeconds,
        System.Diagnostics.Stopwatch stopwatch,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken ct)
    {
        var prepared = await PrepareRunAsync(
            configuration, ownsWorkspace, workingDirectory, promptPath, outputPath, ct).ConfigureAwait(false);
        var process = await RunProcessAsync(
            configuration, prepared.Arguments, workingDirectory, timeoutSeconds, ct).ConfigureAwait(false);
        if (process.Result is null)
            return await PublishRunnerFailureAsync(
                configuration, process, prepared.BaseCommitSha, stopwatch, onOutput, ct).ConfigureAwait(false);
        return await PublishRunnerResultAsync(
            configuration, process.Result, outputPath, workingDirectory,
            prepared.BaseCommitSha, stopwatch, onOutput, ct).ConfigureAwait(false);
    }

    private async Task<PreparedAiRun> PrepareRunAsync(
        AiRunConfiguration configuration,
        bool ownsWorkspace,
        string workingDirectory,
        string promptPath,
        string outputPath,
        CancellationToken ct)
    {
        string? baseCommitSha = null;
        if (configuration.SourceRepositoryPath is not null)
        {
            if (!ownsWorkspace)
                throw new InvalidOperationException(
                    "AI source checkout cannot replace an existing pipeline workspace.");
            baseCommitSha = await CheckoutSourceAsync(configuration, workingDirectory, ct).ConfigureAwait(false);
        }
        var prompt = await BuildPromptAsync(configuration, workingDirectory, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(promptPath, prompt, Encoding.UTF8, ct).ConfigureAwait(false);
        var arguments = configuration.Arguments.Select(argument => argument switch
        {
            "{prompt_file}" => promptPath,
            "{output_file}" => outputPath,
            "{workdir}" => workingDirectory,
            _ => argument
        }).ToList();
        return new PreparedAiRun(arguments, baseCommitSha);
    }

    private async Task<AiProcessOutcome> RunProcessAsync(
        AiRunConfiguration configuration,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        int timeoutSeconds,
        CancellationToken ct)
    {
        try
        {
            var result = await shellRunner.RunExecAsync(
                configuration.Binary,
                arguments,
                configuration.RunnerEnvironment,
                workingDirectory,
                inheritEnvironment: false,
                configuration.MaxOutputBytes,
                ct,
                TimeSpan.FromSeconds(timeoutSeconds)).ConfigureAwait(false);
            return AiProcessOutcome.Succeeded(result);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AiProcessOutcome.Failed("AI runner timed out.", true, false);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return AiProcessOutcome.Failed(
                $"AI runner binary '{configuration.Binary}' is unavailable.", false, true);
        }
    }

    private async Task<ExecutorResult> PublishRunnerFailureAsync(
        AiRunConfiguration configuration,
        AiProcessOutcome outcome,
        string? baseCommitSha,
        System.Diagnostics.Stopwatch stopwatch,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken ct)
    {
        stopwatch.Stop();
        if (outcome.LogAsError)
            await onOutput(outcome.Error!, TaskLogLevel.Error).ConfigureAwait(false);
        return await PublishAsync(
            configuration, outcome.Error!, null, AiVerdict.None,
            false, outcome.TimedOut, baseCommitSha, stopwatch.ElapsedMilliseconds,
            onOutput, ct).ConfigureAwait(false);
    }

    private async Task<ExecutorResult> PublishRunnerResultAsync(
        AiRunConfiguration configuration,
        ShellExecResult process,
        string outputPath,
        string workingDirectory,
        string? baseCommitSha,
        System.Diagnostics.Stopwatch stopwatch,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken ct)
    {
        await ForwardOutputAsync(process.StdOut, TaskLogLevel.Info, onOutput).ConfigureAwait(false);
        await ForwardOutputAsync(process.StdErr, TaskLogLevel.Error, onOutput).ConfigureAwait(false);
        var outputFileExists = File.Exists(outputPath);
        var truncated = process.Truncated
                        || outputFileExists && new FileInfo(outputPath).Length > configuration.MaxOutputBytes;
        var report = outputFileExists
            ? await ReadBoundedFileAsync(outputPath, configuration.MaxOutputBytes, ct).ConfigureAwait(false)
            : process.StdOut;
        report = TruncateUtf8(report, configuration.MaxOutputBytes, ref truncated);
        var diff = await CaptureDiffAsync(workingDirectory, configuration.MaxOutputBytes, ct).ConfigureAwait(false);
        if (diff is not null)
            diff = TruncateUtf8(diff, configuration.MaxOutputBytes, ref truncated);
        stopwatch.Stop();
        var verdict = ParseVerdict(report);
        var succeeded = process.ExitCode == 0
                        && !string.IsNullOrWhiteSpace(report)
                        && (!configuration.Gate || verdict == AiVerdict.Pass);
        if (configuration.Gate && verdict != AiVerdict.Pass)
            await onOutput("AI gate failed: VERDICT: PASS is required.", TaskLogLevel.Error).ConfigureAwait(false);
        return await PublishAsync(
            configuration,
            string.IsNullOrWhiteSpace(report) ? "AI runner produced no report." : report,
            diff, verdict, succeeded, truncated, baseCommitSha, stopwatch.ElapsedMilliseconds,
            onOutput, ct).ConfigureAwait(false);
    }

    private void CleanupTemporaryDirectory(string tempRoot)
    {
        try
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove AI run temporary directory {Directory}", tempRoot);
        }
    }

    private sealed record PreparedAiRun(IReadOnlyList<string> Arguments, string? BaseCommitSha);

    private sealed record AiProcessOutcome(
        ShellExecResult? Result, string? Error, bool TimedOut, bool LogAsError)
    {
        public static AiProcessOutcome Succeeded(ShellExecResult result) => new(result, null, false, false);
        public static AiProcessOutcome Failed(string error, bool timedOut, bool logAsError) =>
            new(null, error, timedOut, logAsError);
    }

    private async Task<ExecutorResult> PublishAsync(
        AiRunConfiguration configuration,
        string report,
        string? diff,
        AiVerdict verdict,
        bool succeeded,
        bool truncated,
        string? baseCommitSha,
        long durationMs,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken ct)
    {
        var published = await apiClient.PublishAiRunResultAsync(new PublishAiRunResultRequest
        {
            ServerTaskId = configuration.ServerTaskId,
            ProfileName = configuration.ProfileName,
            SendsDataExternally = configuration.SendsDataExternally,
            ReportMarkdown = report,
            Verdict = verdict,
            DiffPatch = diff,
            DurationMs = durationMs,
            SourceRepositoryId = configuration.SourceRepositoryId,
            BaseCommitSha = baseCommitSha,
            Truncated = truncated,
            Succeeded = succeeded
        }, ct).ConfigureAwait(false);
        if (published is null)
        {
            await onOutput("AI result could not be persisted.", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }
        return new ExecutorResult(succeeded ? 0 : -1, false);
    }

    private async Task<string?> CaptureDiffAsync(
        string workingDirectory, int maxBytes, CancellationToken ct)
    {
        if (!Directory.Exists(Path.Combine(workingDirectory, ".git"))) return null;
        var tracked = await shellRunner.RunExecAsync(
            "git",
            ["-C", workingDirectory, "diff", "--binary", "HEAD"],
            new Dictionary<string, string>(),
            workingDirectory,
            inheritEnvironment: true,
            maxBytes,
            ct,
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        var builder = new StringBuilder(tracked.StdOut);
        var untracked = await shellRunner.RunExecAsync(
            "git",
            ["-C", workingDirectory, "ls-files", "--others", "--exclude-standard"],
            new Dictionary<string, string>(),
            workingDirectory,
            inheritEnvironment: true,
            maxBytes,
            ct,
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        foreach (var relativePath in untracked.StdOut.Split(
                     ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(100))
        {
            if (!IsSafeRelativePath(relativePath)) continue;
            var fileDiff = await shellRunner.RunExecAsync(
                "git",
                ["-C", workingDirectory, "diff", "--binary", "--no-index", "--", "/dev/null", relativePath],
                new Dictionary<string, string>(),
                workingDirectory,
                inheritEnvironment: true,
                maxBytes,
                ct,
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            builder.Append(fileDiff.StdOut);
            if (Encoding.UTF8.GetByteCount(builder.ToString()) >= maxBytes) break;
        }
        return builder.Length == 0 ? null : builder.ToString();
    }

    private async Task<string> CheckoutSourceAsync(
        AiRunConfiguration configuration,
        string workingDirectory,
        CancellationToken ct)
    {
        var sourcePath = configuration.SourceRepositoryPath
            ?? throw new InvalidOperationException("AI source repository path is missing.");
        var sourceUrl = $"{_options.ServerUrl.TrimEnd('/')}/{sourcePath.TrimStart('/')}";
        var clone = await shellRunner.RunExecAsync(
            "git",
            [
                "clone",
                "--single-branch",
                "--branch",
                configuration.SourceRef ?? "main",
                "--",
                sourceUrl,
                workingDirectory
            ],
            BuildGitAuthenticationEnvironment(configuration),
            workingDirectory,
            inheritEnvironment: false,
            64 * 1024,
            ct,
            TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        if (clone.ExitCode != 0)
            throw new InvalidOperationException("AI source repository checkout failed.");
        var revision = await shellRunner.RunExecAsync(
            "git",
            ["-C", workingDirectory, "rev-parse", "HEAD"],
            ct,
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        var commit = revision.StdOut.Trim();
        if (revision.ExitCode != 0
            || commit.Length is not (40 or 64)
            || commit.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("AI source checkout did not produce a valid commit.");
        return commit;
    }

    private static Dictionary<string, string> BuildGitAuthenticationEnvironment(
        AiRunConfiguration configuration)
    {
        if (configuration.GitUsername is null || configuration.GitPassword is null)
            throw new InvalidOperationException("AI source checkout credentials are missing.");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{configuration.GitUsername}:{configuration.GitPassword}"));
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.extraHeader",
            ["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {basic}",
            ["GIT_TERMINAL_PROMPT"] = "0"
        };
    }

    private static async Task<string> ReadBoundedFileAsync(
        string path,
        int maximumBytes,
        CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var bytesToRead = (int)Math.Min(stream.Length, (long)maximumBytes + 1);
        var buffer = new byte[bytesToRead];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return Encoding.UTF8.GetString(buffer.AsSpan(0, Math.Min(total, maximumBytes)));
    }

    private static async Task<string> BuildPromptAsync(
        AiRunConfiguration configuration, string workingDirectory, CancellationToken ct)
    {
        var builder = new StringBuilder(configuration.Prompt);
        if (!string.IsNullOrWhiteSpace(configuration.PromptFile))
        {
            if (!IsSafeRelativePath(configuration.PromptFile))
                throw new InvalidOperationException("AI prompt_file is not a safe relative path.");
            var fullPath = Path.GetFullPath(Path.Combine(workingDirectory, configuration.PromptFile));
            if (!fullPath.StartsWith(
                    Path.GetFullPath(workingDirectory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("AI prompt_file escapes the workspace.");
            builder.AppendLine();
            builder.AppendLine(await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false));
        }
        if (configuration.ContextPaths.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Explicit context paths:");
            foreach (var path in configuration.ContextPaths)
                builder.AppendLine($"- {path}");
        }
        return builder.ToString();
    }

    private static async Task ForwardOutputAsync(
        string output,
        TaskLogLevel level,
        Func<string, TaskLogLevel, Task> onOutput)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            await onOutput(line, level).ConfigureAwait(false);
    }

    private static bool TryReadConfiguration(
        IReadOnlyDictionary<string, string> env,
        out AiRunConfiguration configuration,
        out string error)
    {
        configuration = default!;
        error = "AI run configuration is incomplete.";
        if (!TryReadRequiredConfiguration(env, out var required)) return false;
        List<string>? arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<List<string>>(required.ArgumentsJson);
        }
        catch (JsonException)
        {
            return false;
        }
        if (arguments is null || arguments.Count > 64) return false;
        var contextPaths = ReadJsonList(env.GetValueOrDefault("AETHEUS_AI_CONTEXT_PATHS_JSON"));
        if (!TryReadSourceRepository(
                env, out var sourceRepositoryId, out var sourceRepositoryPath, out var sourceRef))
            return false;
        var runnerEnvironment = ReadRunnerEnvironment(env);
        configuration = new AiRunConfiguration(
            required.TaskId,
            required.ProfileName,
            required.Binary,
            arguments,
            required.Prompt,
            env.GetValueOrDefault("AETHEUS_AI_PROMPT_FILE"),
            contextPaths,
            env.GetValueOrDefault("AETHEUS_AI_WORKING_DIR") ?? string.Empty,
            string.Equals(env.GetValueOrDefault("AETHEUS_AI_GATE"), "true", StringComparison.OrdinalIgnoreCase),
            string.Equals(env.GetValueOrDefault("AETHEUS_AI_SENDS_DATA_EXTERNALLY"), "true", StringComparison.OrdinalIgnoreCase),
            Math.Clamp(required.MaxOutputBytes, 1024, 1_000_000),
            runnerEnvironment,
            sourceRepositoryId,
            sourceRepositoryPath,
            sourceRef,
            env.GetValueOrDefault("GIT_USERNAME"),
            env.GetValueOrDefault("GIT_PASSWORD"));
        return true;
    }

    private static bool TryReadRequiredConfiguration(
        IReadOnlyDictionary<string, string> env, out RequiredAiConfiguration configuration)
    {
        configuration = default!;
        if (!env.TryGetValue("AETHEUS_TASK_ID", out var taskText)
            || !int.TryParse(taskText, out var taskId)
            || !env.TryGetValue("AETHEUS_AI_PROFILE_NAME", out var profileName)
            || !env.TryGetValue("AETHEUS_AI_BINARY", out var binary)
            || !env.TryGetValue("AETHEUS_AI_ARGS_JSON", out var argsJson)
            || !env.TryGetValue("AETHEUS_AI_PROMPT", out var prompt)
            || !env.TryGetValue("AETHEUS_AI_MAX_OUTPUT_BYTES", out var maxText)
            || !int.TryParse(maxText, out var maxOutputBytes)) return false;
        configuration = new RequiredAiConfiguration(
            taskId, profileName, binary, argsJson, prompt, maxOutputBytes);
        return true;
    }

    private static List<string> ReadJsonList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private sealed record RequiredAiConfiguration(
        int TaskId, string ProfileName, string Binary, string ArgumentsJson, string Prompt, int MaxOutputBytes);

    private static bool TryReadSourceRepository(
        IReadOnlyDictionary<string, string> env,
        out int? sourceRepositoryId,
        out string? sourceRepositoryPath,
        out string? sourceRef)
    {
        sourceRepositoryId = null;
        sourceRepositoryPath = null;
        sourceRef = null;
        var hasSourceRepositoryId = env.TryGetValue(
            "AETHEUS_AI_SOURCE_REPOSITORY_ID",
            out var sourceRepositoryText);
        var hasSourceRepositoryPath = env.TryGetValue(
            "AETHEUS_AI_SOURCE_REPOSITORY_PATH",
            out sourceRepositoryPath);
        if (hasSourceRepositoryId || hasSourceRepositoryPath)
        {
            if (!hasSourceRepositoryId
                || !hasSourceRepositoryPath
                || !int.TryParse(sourceRepositoryText, out var parsedSourceRepositoryId)
                || parsedSourceRepositoryId <= 0
                || string.IsNullOrWhiteSpace(sourceRepositoryPath)
                || !sourceRepositoryPath.StartsWith("/git/", StringComparison.Ordinal)
                || sourceRepositoryPath.Contains("..", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(env.GetValueOrDefault("GIT_USERNAME"))
                || string.IsNullOrWhiteSpace(env.GetValueOrDefault("GIT_PASSWORD")))
                return false;
            sourceRepositoryId = parsedSourceRepositoryId;
            sourceRef = env.GetValueOrDefault("AETHEUS_AI_SOURCE_REF");
        }
        return true;
    }

    private static Dictionary<string, string> ReadRunnerEnvironment(
        IReadOnlyDictionary<string, string> env) =>
        env.Where(variable =>
                !variable.Key.StartsWith("AETHEUS_", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(variable.Key, "GIT_USERNAME", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(variable.Key, "GIT_PASSWORD", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);

    internal static AiVerdict ParseVerdict(string report)
    {
        var verdict = AiVerdict.None;
        foreach (var line in report.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(line.Trim(), "VERDICT: PASS", StringComparison.Ordinal))
                verdict = verdict == AiVerdict.Fail ? AiVerdict.Fail : AiVerdict.Pass;
            else if (string.Equals(line.Trim(), "VERDICT: FAIL", StringComparison.Ordinal))
                verdict = AiVerdict.Fail;
        }
        return verdict;
    }

    private static string TruncateUtf8(string value, int maxBytes, ref bool truncated)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes) return value;
        truncated = true;
        return Encoding.UTF8.GetString(bytes.AsSpan(0, maxBytes));
    }

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && !path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");

    private sealed record AiRunConfiguration(
        int ServerTaskId,
        string ProfileName,
        string Binary,
        List<string> Arguments,
        string Prompt,
        string? PromptFile,
        List<string> ContextPaths,
        string WorkingDirectory,
        bool Gate,
        bool SendsDataExternally,
        int MaxOutputBytes,
        Dictionary<string, string> RunnerEnvironment,
        int? SourceRepositoryId,
        string? SourceRepositoryPath,
        string? SourceRef,
        string? GitUsername,
        string? GitPassword);
}
