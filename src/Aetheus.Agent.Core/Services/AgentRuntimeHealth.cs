// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace Aetheus.Agent.Core.Services;

public sealed class AgentRuntimeHealth(TimeProvider timeProvider)
{
    private long _lastHeartbeatSuccessUtcTicks;
    private long _lastPollingSuccessUtcTicks;
    private int _activeTaskCount;
    private long _processRegistrationSequence;
    private readonly ConcurrentDictionary<long, ProcessIdentity> _activeProcesses = new();

    public void BeginHeartbeatGracePeriod() => MarkHeartbeatSuccess();

    public void BeginPollingGracePeriod() => MarkPollingSuccess();

    public void MarkHeartbeatSuccess() =>
        Interlocked.Exchange(ref _lastHeartbeatSuccessUtcTicks, timeProvider.GetUtcNow().UtcTicks);

    public void MarkPollingSuccess() =>
        Interlocked.Exchange(ref _lastPollingSuccessUtcTicks, timeProvider.GetUtcNow().UtcTicks);

    public void BeginTask() => Interlocked.Increment(ref _activeTaskCount);

    public void EndTask() => Interlocked.Decrement(ref _activeTaskCount);

    public int ActiveTaskCount => Math.Max(0, Volatile.Read(ref _activeTaskCount));

    public int ActiveProcessCount
    {
        get
        {
            RemoveExitedProcesses();
            return _activeProcesses.Count;
        }
    }

    public bool IsIdle => ActiveTaskCount == 0 && ActiveProcessCount == 0;

    internal IDisposable TrackProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var registrationId = Interlocked.Increment(ref _processRegistrationSequence);
        _activeProcesses[registrationId] = new ProcessIdentity(process.Id, ReadStartTimeTicks(process));
        return new ProcessRegistration(this, registrationId);
    }

    public DateTimeOffset? LastHeartbeatSuccessAt => Read(ref _lastHeartbeatSuccessUtcTicks);

    public DateTimeOffset? LastPollingSuccessAt => Read(ref _lastPollingSuccessUtcTicks);

    private static DateTimeOffset? Read(ref long ticks)
    {
        var value = Volatile.Read(ref ticks);
        return value == 0 ? null : new DateTimeOffset(value, TimeSpan.Zero);
    }

    private void RemoveExitedProcesses()
    {
        foreach (var registration in _activeProcesses)
        {
            if (!IsRunning(registration.Value))
                _activeProcesses.TryRemove(registration.Key, out _);
        }
    }

    private static bool IsRunning(ProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            if (process.HasExited)
                return false;
            return identity.StartTimeUtcTicks == 0
                || ReadStartTimeTicks(process) == identity.StartTimeUtcTicks;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }

    private static long ReadStartTimeTicks(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return 0;
        }
    }

    private sealed class ProcessRegistration(AgentRuntimeHealth owner, long registrationId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner._activeProcesses.TryRemove(registrationId, out _);
        }
    }

    private readonly record struct ProcessIdentity(int ProcessId, long StartTimeUtcTicks);
}
