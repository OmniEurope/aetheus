// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Agent.Core.Operations;

/// <summary>
/// PLAN-006 4.3: runs a backup (DB dump + optional file archive) and a restore-check on the target host.
/// No sudo: pg_dump/mysqldump run as a normal DB client; the password rides in <c>PGPASSWORD</c>/
/// <c>MYSQL_PWD</c> in the process env (off the process list), never in argv. The restore-check restores
/// onto a REAL throwaway DB created for the test and dropped after - a backup is only "verified" when that
/// actually succeeds (no-fake). Results are reported to the backend, which owns the run state machine.
/// </summary>
public sealed class BackupOperationExecutor(
    IServerApiClient apiClient,
    ILogger<BackupOperationExecutor> logger) : IOperationExecutor
{
    public bool CanHandle(OperationKind kind) => kind is OperationKind.BackupExecute or OperationKind.BackupRestoreCheck or OperationKind.BackupRestore;

    public Task<ExecutorResult> ExecuteAsync(
        OperationKind kind, string target, int timeoutSeconds,
        Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
        => ExecuteAsync(kind, target, new Dictionary<string, string>(), timeoutSeconds, onOutput, ct);

    public async Task<ExecutorResult> ExecuteAsync(
        OperationKind kind, string target, IReadOnlyDictionary<string, string> env,
        int timeoutSeconds, Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await onOutput("Backups are only supported on Linux", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        return kind switch
        {
            OperationKind.BackupExecute => await RunBackupAsync(env, timeoutSeconds, onOutput, ct).ConfigureAwait(false),
            OperationKind.BackupRestoreCheck => await RunRestoreCheckAsync(env, timeoutSeconds, onOutput, ct).ConfigureAwait(false),
            OperationKind.BackupRestore => await RunLiveRestoreAsync(env, timeoutSeconds, onOutput, ct).ConfigureAwait(false),
            _ => new ExecutorResult(1, false)
        };
    }

    private async Task<ExecutorResult> RunBackupAsync(
        IReadOnlyDictionary<string, string> env, int timeoutSeconds, Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        var engine = ParseEngine(env);
        var runId = GetInt(env, BackupConstants.RunIdEnvVar);
        var policyId = GetInt(env, BackupConstants.PolicyIdEnvVar);
        if (runId is null || policyId is null)
        {
            await onOutput("Missing backup run/policy id", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(-1, false);
        }

        var dir = Path.Combine(BackupConstants.BaseDir, policyId.Value.ToString());
        Directory.CreateDirectory(dir);
        var archive = Path.Combine(dir, $"backup-{runId}.{(engine == BackupDbEngine.MySql ? "sql" : "dump")}");

        var (host, port, name, user, pwd) = DbParams(env, engine);
        ExecutorResult dump;
        if (engine == BackupDbEngine.Postgres)
            dump = await RunAsync("pg_dump", BackupCommandBuilder.PgDump(host, port, user, name, archive), PgEnv(pwd), timeoutSeconds, onOutput, ct).ConfigureAwait(false);
        else if (engine == BackupDbEngine.MySql)
            dump = await RunAsync("mysqldump", BackupCommandBuilder.MysqlDump(host, port, user, name, archive), MysqlEnv(pwd), timeoutSeconds, onOutput, ct).ConfigureAwait(false);
        else
        {
            await onOutput("No DB engine configured; files-only backups are not yet supported", TaskLogLevel.Warning).ConfigureAwait(false);
            await apiClient.ReportBackupResultAsync(runId.Value, new BackupExecuteResultDto { Success = false, Message = "No DB engine configured." }, ct).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        if (dump.ExitCode != 0)
        {
            await apiClient.ReportBackupResultAsync(runId.Value, new BackupExecuteResultDto { Success = false, Message = $"Dump failed (exit {dump.ExitCode})." }, ct).ConfigureAwait(false);
            return dump;
        }

        var size = new FileInfo(archive).Length;
        var sha = await ComputeSha256Async(archive, ct).ConfigureAwait(false);
        PruneOldArchives(dir, GetInt(env, BackupConstants.RetentionEnvVar) ?? 7, onOutput);

        await onOutput($"Backup complete: {archive} ({size} bytes)", TaskLogLevel.Info).ConfigureAwait(false);
        await apiClient.ReportBackupResultAsync(runId.Value,
            new BackupExecuteResultDto { Success = true, ArchivePath = archive, SizeBytes = size, Sha256 = sha }, ct).ConfigureAwait(false);
        return new ExecutorResult(0, false);
    }

    private async Task<ExecutorResult> RunRestoreCheckAsync(
        IReadOnlyDictionary<string, string> env, int timeoutSeconds, Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        var engine = ParseEngine(env);
        var runId = GetInt(env, BackupConstants.RunIdEnvVar);
        env.TryGetValue(BackupConstants.ArchivePathEnvVar, out var archive);
        if (runId is null || string.IsNullOrEmpty(archive) || !File.Exists(archive))
        {
            if (runId is not null)
                await apiClient.ReportRestoreCheckResultAsync(runId.Value, new RestoreCheckResultDto { Verified = false, Message = "Archive not found." }, ct).ConfigureAwait(false);
            await onOutput("Restore-check: archive not found", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        var (host, port, _, user, pwd) = DbParams(env, engine);
        var scratch = $"aetheus_restorecheck_{runId}";
        var verified = false;
        var message = "";
        try
        {
            if (engine == BackupDbEngine.Postgres)
            {
                await RunAsync("dropdb", ["-h", host, "-p", port.ToString(), "-U", user, "--if-exists", scratch], PgEnv(pwd), timeoutSeconds, onOutput, ct).ConfigureAwait(false);
                var create = await RunAsync("createdb", ["-h", host, "-p", port.ToString(), "-U", user, scratch], PgEnv(pwd), timeoutSeconds, onOutput, ct).ConfigureAwait(false);
                if (create.ExitCode != 0) { message = "Could not create scratch database."; }
                else
                {
                    var restore = await RunAsync("pg_restore", BackupCommandBuilder.PgRestore(host, port, user, scratch, archive), PgEnv(pwd), timeoutSeconds, onOutput, ct).ConfigureAwait(false);
                    verified = restore.ExitCode == 0;
                    message = verified ? "Restore verified on a throwaway database." : $"Restore failed (exit {restore.ExitCode}).";
                }
            }
            else if (engine == BackupDbEngine.MySql)
            {
                await RunAsync("mysql", ["-h", host, "-P", port.ToString(), "-u", user, "-e", $"DROP DATABASE IF EXISTS {scratch}; CREATE DATABASE {scratch};"], MysqlEnv(pwd), timeoutSeconds, onOutput, ct).ConfigureAwait(false);
                var restore = await RunAsync("mysql", BackupCommandBuilder.MysqlRestore(host, port, user, scratch), MysqlEnv(pwd), timeoutSeconds, onOutput, ct, stdinFile: archive).ConfigureAwait(false);
                verified = restore.ExitCode == 0;
                message = verified ? "Restore verified on a throwaway database." : $"Restore failed (exit {restore.ExitCode}).";
            }
            else
            {
                message = "No DB engine configured.";
            }
        }
        finally
        {
            // Always drop the throwaway target, even on failure.
            if (engine == BackupDbEngine.Postgres)
                await RunAsync("dropdb", ["-h", host, "-p", port.ToString(), "-U", user, "--if-exists", scratch], PgEnv(pwd), timeoutSeconds, onOutput, ct).ConfigureAwait(false);
            else if (engine == BackupDbEngine.MySql)
                await RunAsync("mysql", ["-h", host, "-P", port.ToString(), "-u", user, "-e", $"DROP DATABASE IF EXISTS {scratch};"], MysqlEnv(pwd), timeoutSeconds, onOutput, ct).ConfigureAwait(false);
        }

        await onOutput($"Restore-check: {(verified ? "VERIFIED" : "FAILED")} - {message}", verified ? TaskLogLevel.Info : TaskLogLevel.Error).ConfigureAwait(false);
        await apiClient.ReportRestoreCheckResultAsync(runId.Value, new RestoreCheckResultDto { Verified = verified, Message = message }, ct).ConfigureAwait(false);
        return new ExecutorResult(verified ? 0 : 1, false);
    }

    private async Task<ExecutorResult> RunLiveRestoreAsync(
        IReadOnlyDictionary<string, string> env, int timeoutSeconds, Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        var engine = ParseEngine(env);
        env.TryGetValue(BackupConstants.ArchivePathEnvVar, out var archive);
        if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
        {
            await onOutput("Database restore refused: verified backup archive is not present on this server.", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        var (host, port, name, user, pwd) = DbParams(env, engine);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(user))
        {
            await onOutput("Database restore refused: database name or user is missing.", TaskLogLevel.Error).ConfigureAwait(false);
            return new ExecutorResult(1, false);
        }

        await onOutput($"Restoring verified backup into '{name}' (explicit rollback request)…", TaskLogLevel.Warning).ConfigureAwait(false);
        var restore = engine switch
        {
            BackupDbEngine.Postgres => await RunAsync("pg_restore", BackupCommandBuilder.PgRestore(host, port, user, name, archive), PgEnv(pwd), timeoutSeconds, onOutput, ct).ConfigureAwait(false),
            BackupDbEngine.MySql => await RunAsync("mysql", BackupCommandBuilder.MysqlRestore(host, port, user, name), MysqlEnv(pwd), timeoutSeconds, onOutput, ct, stdinFile: archive).ConfigureAwait(false),
            _ => new ExecutorResult(1, false)
        };
        await onOutput(restore.ExitCode == 0 ? "Database restore completed." : "Database restore failed.", restore.ExitCode == 0 ? TaskLogLevel.Warning : TaskLogLevel.Error).ConfigureAwait(false);
        return restore;
    }

    private async Task<ExecutorResult> RunAsync(
        string fileName, IReadOnlyList<string> argv, (string Key, string Value)? extraEnv,
        int timeoutSeconds, Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct, string? stdinFile = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinFile is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in argv) psi.ArgumentList.Add(a);
        if (extraEnv is { } e) psi.Environment[e.Key] = e.Value;

        if (stdinFile is null)
            return await ProcessRunner.RunAsync(psi, timeoutSeconds, onOutput, logger, ct).ConfigureAwait(false);

        // mysql restore: feed the dump file on stdin. Drain stdout/stderr concurrently (a chatty mysql
        // otherwise blocks on a full stderr pipe while we're still writing stdin => deadlock) and enforce
        // the timeout with a kill so a hung client can't wedge the restore-check forever.
        using var process = Process.Start(psi)!;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));
        var token = timeoutCts.Token;

        var drain = Task.WhenAll(
            DrainAsync(process.StandardOutput, TaskLogLevel.Info, onOutput, token),
            DrainAsync(process.StandardError, TaskLogLevel.Error, onOutput, token));

        try
        {
            await using (var input = File.OpenRead(stdinFile))
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
            // TimedOut only when the linked timeout fired, not when the caller cancelled the whole task.
            return new ExecutorResult(-1, !ct.IsCancellationRequested);
        }
    }

    private static async Task DrainAsync(StreamReader reader, TaskLogLevel level, Func<string, TaskLogLevel, Task> onOutput, CancellationToken ct)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                await onOutput(line, level).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* process killed on timeout/cancel */ }
        catch (Exception) { /* stream closed under us */ }
    }

    private static (string Key, string Value)? PgEnv(string? pwd) => pwd is null ? null : ("PGPASSWORD", pwd);
    private static (string Key, string Value)? MysqlEnv(string? pwd) => pwd is null ? null : ("MYSQL_PWD", pwd);

    private static (string Host, int Port, string Name, string User, string? Pwd) DbParams(IReadOnlyDictionary<string, string> env, BackupDbEngine engine)
    {
        env.TryGetValue(BackupConstants.DbHostEnvVar, out var host);
        env.TryGetValue(BackupConstants.DbNameEnvVar, out var name);
        env.TryGetValue(BackupConstants.DbUserEnvVar, out var user);
        env.TryGetValue(BackupConstants.DbPasswordEnvVar, out var pwd);
        var port = GetInt(env, BackupConstants.DbPortEnvVar) ?? (engine == BackupDbEngine.MySql ? 3306 : 5432);
        return (string.IsNullOrEmpty(host) ? "127.0.0.1" : host, port, name ?? "", user ?? "", string.IsNullOrEmpty(pwd) ? null : pwd);
    }

    private static BackupDbEngine ParseEngine(IReadOnlyDictionary<string, string> env)
        => env.TryGetValue(BackupConstants.EngineEnvVar, out var e) && Enum.TryParse<BackupDbEngine>(e, out var parsed)
            ? parsed : BackupDbEngine.None;

    private static int? GetInt(IReadOnlyDictionary<string, string> env, string key)
        => env.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : null;

    private static async Task<string> ComputeSha256Async(string file, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private void PruneOldArchives(string dir, int keep, Func<string, TaskLogLevel, Task> onOutput)
    {
        try
        {
            var files = new DirectoryInfo(dir).GetFiles("backup-*")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(Math.Max(1, keep))
                .ToList();
            foreach (var f in files)
                f.Delete();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[BackupOperationExecutor] prune failed");
        }
    }
}
