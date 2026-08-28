// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Security.Cryptography;

namespace Aetheus.Agent.Core.Operations;

internal delegate Task<ExecutorResult> BackupProcessInvoker(
    string fileName,
    IReadOnlyList<string> argv,
    (string Key, string Value)? extraEnvironment,
    int timeoutSeconds,
    Func<string, TaskLogLevel, Task> onOutput,
    CancellationToken cancellationToken,
    string? standardInputFile);

internal static class BackupOperationHelpers
{
    internal static async Task DrainAsync(
        StreamReader reader,
        TaskLogLevel level,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
                await onOutput(line, level).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* process killed on timeout/cancel */ }
        catch (Exception) { /* stream closed under us */ }
    }

    internal static (string Key, string Value)? PgEnv(string? password) =>
        password is null ? null : ("PGPASSWORD", password);

    internal static (string Key, string Value)? MysqlEnv(string? password) =>
        password is null ? null : ("MYSQL_PWD", password);

    internal static async Task<ExecutorResult> RunProcessAsync(
        BackupProcessInvoker? processInvoker,
        ILogger logger,
        string fileName,
        IReadOnlyList<string> argv,
        (string Key, string Value)? extraEnvironment,
        int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken,
        string? standardInputFile)
    {
        if (processInvoker is not null)
        {
            return await processInvoker(
                fileName, argv, extraEnvironment, timeoutSeconds, onOutput,
                cancellationToken, standardInputFile).ConfigureAwait(false);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInputFile is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in argv) startInfo.ArgumentList.Add(argument);
        if (extraEnvironment is { } environment)
            startInfo.Environment[environment.Key] = environment.Value;

        if (standardInputFile is null)
            return await ProcessRunner.RunAsync(
                startInfo, timeoutSeconds, onOutput, logger, cancellationToken).ConfigureAwait(false);

        using var process = Process.Start(startInfo)!;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        var token = timeoutCts.Token;
        var drain = Task.WhenAll(
            DrainAsync(process.StandardOutput, TaskLogLevel.Info, onOutput, token),
            DrainAsync(process.StandardError, TaskLogLevel.Error, onOutput, token));

        try
        {
            await using (var input = File.OpenRead(standardInputFile))
                await input.CopyToAsync(process.StandardInput.BaseStream, token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await drain.ConfigureAwait(false);
            return new ExecutorResult(process.ExitCode, false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            await onOutput("mysql restore timed out or was cancelled.", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, !cancellationToken.IsCancellationRequested);
        }
    }

    internal static void PruneOldArchives(string directory, int keep, ILogger logger)
    {
        try
        {
            foreach (var archive in GetArchivesToPrune(directory, keep))
                archive.Delete();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[BackupOperationExecutor] prune failed");
        }
    }

    internal static IReadOnlyList<FileInfo> GetArchivesToPrune(string directory, int keep) =>
        new DirectoryInfo(directory).GetFiles("backup-*.aetheus-backup")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(Math.Max(1, keep))
            .ToList();

    internal static (string Host, int Port, string Name, string User, string? Pwd) DbParams(
        IReadOnlyDictionary<string, string> environment,
        BackupDbEngine engine)
    {
        environment.TryGetValue(BackupConstants.DbHostEnvVar, out var host);
        environment.TryGetValue(BackupConstants.DbNameEnvVar, out var name);
        environment.TryGetValue(BackupConstants.DbUserEnvVar, out var user);
        environment.TryGetValue(BackupConstants.DbPasswordEnvVar, out var password);
        var port = GetInt(environment, BackupConstants.DbPortEnvVar)
            ?? (engine == BackupDbEngine.MySql ? 3306 : 5432);
        return (
            string.IsNullOrEmpty(host) ? "127.0.0.1" : host,
            port,
            name ?? "",
            user ?? "",
            string.IsNullOrEmpty(password) ? null : password);
    }

    internal static BackupDbEngine ParseEngine(IReadOnlyDictionary<string, string> environment) =>
        environment.TryGetValue(BackupConstants.EngineEnvVar, out var value)
        && Enum.TryParse<BackupDbEngine>(value, out var parsed)
            ? parsed
            : BackupDbEngine.None;

    internal static int? GetInt(IReadOnlyDictionary<string, string> environment, string key) =>
        environment.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : null;

    internal static async Task<string> ComputeSha256Async(string file, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    internal static async Task RestoreBundleFilesAsync(
        VerifiedBackupBundle? bundle,
        Func<string, TaskLogLevel, Task> onOutput,
        CancellationToken cancellationToken,
        Action<BackupFileRestoreBoundary, int>? boundaryHook)
    {
        if (bundle is null || bundle.Sources.Count == 0)
            return;
        await BackupArchiveBundle.RestoreFilesAsync(bundle, cancellationToken, boundaryHook).ConfigureAwait(false);
        await onOutput($"Restored {bundle.Sources.Count} file source(s).", TaskLogLevel.Warning).ConfigureAwait(false);
    }

    internal static void DeleteRollbackDump(string? rollbackDump, ILogger logger)
    {
        if (rollbackDump is null || !File.Exists(rollbackDump))
            return;
        try { File.Delete(rollbackDump); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete database compensation snapshot {Path}", rollbackDump);
        }
    }
}
