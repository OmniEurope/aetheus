// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers;

/// <summary>
/// Recette R-510: every action the Services page launches (one row's button or the bulk bar) is
/// followed per service, so each row says what is happening to it: queued, running, succeeded, or
/// failed with its exit code. The page used to follow a single action at a time and said nothing of
/// the others beyond a "task queued" toast.
///
/// A task is matched by its id. Whether a queued task has started is read from the live task list
/// the top bar already keeps (<see cref="TaskTrackerService"/>), so nothing more is subscribed.
/// </summary>
internal sealed class ServiceActionTracker
{
    /// <summary>
    /// Recette R2-030: how long a success stays on its row at least. The success refreshes the server
    /// at once, so the list that reflects it arrives a moment later and used to take the line away
    /// before it could be read.
    /// </summary>
    internal static readonly TimeSpan SuccessShownAtLeast = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, ServiceActionState> _byService = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _succeededAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Starts following <paramref name="taskId"/> as the current action of the service.</summary>
    internal void Track(string service, int taskId, string actionKey)
    {
        _byService[service] = new ServiceActionState(taskId, actionKey, ServiceActionPhase.Queued);
        _succeededAt.Remove(service);
    }

    /// <summary>
    /// Records the outcome of a finished task at <paramref name="now"/>. Returns the service it was
    /// acting on, or null when the task is not one this page launched.
    /// </summary>
    internal string? Complete(TaskCompletedNotification notification, DateTimeOffset now)
    {
        var (service, state) = _byService.FirstOrDefault(entry => entry.Value.TaskId == notification.TaskId);
        if (service is null || !state.InFlight) return null;
        if (notification.Status == TaskExecutionStatus.Success)
        {
            _byService[service] = state with { Phase = ServiceActionPhase.Succeeded };
            _succeededAt[service] = now;
        }
        else
        {
            _byService[service] = state with { Phase = ServiceActionPhase.Failed, ExitCode = notification.ExitCode };
        }
        return service;
    }

    /// <summary>The state to show on the service's row, or null when nothing was launched on it.</summary>
    internal ServiceActionState? Of(string service, IReadOnlyList<ServerTaskDto> liveTasks)
    {
        if (!_byService.TryGetValue(service, out var state)) return null;
        return state.Phase == ServiceActionPhase.Queued
            && liveTasks.Any(task => task.Id == state.TaskId && task.Status == TaskExecutionStatus.Running)
                ? state with { Phase = ServiceActionPhase.Running }
                : state;
    }

    /// <summary>
    /// Called when a new service list arrives. A success is said until a list that reflects it arrives
    /// once it has been on screen for <see cref="SuccessShownAtLeast"/>; a failure stays on its row
    /// until another action is launched on that service.
    /// </summary>
    internal void ForgetSucceeded(DateTimeOffset now)
    {
        foreach (var service in _succeededAt.Where(entry => now - entry.Value >= SuccessShownAtLeast)
                     .Select(entry => entry.Key).ToList())
        {
            _byService.Remove(service);
            _succeededAt.Remove(service);
        }
    }

    internal void Clear()
    {
        _byService.Clear();
        _succeededAt.Clear();
    }
}
