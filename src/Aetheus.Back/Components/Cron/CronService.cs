// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Validation;
using Cronos;

namespace Aetheus.Back.Components.Cron;

public class CronService(ICronRepository repo, IAuditService audit, ITaskService taskService) : ICronService
{
    private Task QueueTaskAsync(ServerTask task, CancellationToken ct = default)
        => TaskQueuePersistence.PersistAndNotifyAsync(repo.AddTaskAsync, taskService, task, ct);

    public Task<List<CronJobDto>> GetJobsAsync(int serverId, CancellationToken ct = default)
    {
        // Cron jobs are delivered via heartbeat (Server.Cron.Jobs).
        // This endpoint exists for ad-hoc refresh; return empty here.
        return Task.FromResult(new List<CronJobDto>());
    }

    public async Task SaveJobAsync(int serverId, CronJobSaveRequest request, CancellationToken ct = default)
    {
        if (!IsValidNonRootUser(request.User))
            throw new BadRequestException("Invalid cron user (root is not permitted).");
        if (!IsValidSchedule(request.Schedule))
            throw new BadRequestException("Invalid cron schedule.");
        if (!IsValidCommand(request.Command))
            throw new BadRequestException("Invalid cron command.");

        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id;
        if (!IsValidIdentifier(id))
            throw new BadRequestException("Invalid cron job id.");

        // Phase 3: typed CronSave operation. The agent's CronOperationExecutor invokes the root-owned
        // aetheus-cron-apply helper (argv-exact sudoers) which writes /etc/cron.d/aetheus-<id>.
        // No free-form shell (the old `sudo -u … sh -c '… | crontab -'` was rejected by CommandValidator).
        var env = new Dictionary<string, string>
        {
            ["AETHEUS_CRON_USER"] = request.User,
            ["AETHEUS_CRON_SCHEDULE"] = request.Schedule,
            ["AETHEUS_CRON_COMMAND"] = request.Command
        };
        await QueueTaskAsync(
            ServerTaskFactory.Operation(serverId, $"Cron save - {request.User}", OperationKind.CronSave, id, env, 60),
            ct).ConfigureAwait(false);

        await audit.LogAsync("CronSave", "Cron", serverId, request.User, ct).ConfigureAwait(false);
    }

    public async Task DeleteJobAsync(int serverId, CronJobDeleteRequest request, CancellationToken ct = default)
    {
        if (!IsValidUser(request.User))
            throw new BadRequestException("Invalid cron user.");
        if (!IsValidIdentifier(request.Id))
            throw new BadRequestException("Invalid cron job id.");

        await QueueTaskAsync(
            ServerTaskFactory.Operation(serverId, $"Cron delete - {request.Id}", OperationKind.CronDelete, request.Id, 60),
            ct).ConfigureAwait(false);

        await audit.LogAsync("CronDelete", "Cron", serverId, request.Id, ct).ConfigureAwait(false);
    }

    internal static bool IsValidUser(string user) => CronValidation.IsValidUser(user);
    internal static bool IsValidNonRootUser(string user) => CronValidation.IsValidNonRootUser(user);
    internal static bool IsValidIdentifier(string id) => CronValidation.IsValidIdentifier(id);
    internal static bool IsValidCommand(string command) => CronValidation.IsValidCommand(command);

    internal static bool IsValidSchedule(string schedule)
    {
        // Regex (charset + length + 5-field cron.d format) first, then a full Cronos parse to reject
        // syntactically-valid-but-semantically-broken expressions (e.g. "99 99 * * *"). The shared syntax
        // check already guarantees a 5-field schedule, so we parse with the Standard format only - a
        // 6-field (seconds) schedule would corrupt the cron.d file and is rejected upstream. Cronos lives
        // backend-side only.
        if (!CronValidation.IsValidScheduleSyntax(schedule)) return false;
        try
        {
            CronExpression.Parse(schedule, CronFormat.Standard);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }
}
