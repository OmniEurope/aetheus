// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Microsoft.AspNetCore.Components.Routing;

namespace Aetheus.Front.Services;

/// <summary>
/// Item #1 of the plan - scaffolding for the URL refactor that replaces <c>?section=</c> with
/// real routes (<c>/servers/{id}/overview</c>, <c>/servers/{id}/services</c>, …).
///
/// The 21 future section pages all share a single ServerDetailLayout, and that layout owns:
///   - the one-shot <c>GetServerDetailAsync(id)</c> fetch
///   - the SignalR <c>server-{id}</c> group join / leave
///   - the capability flags (HasDocker, HasApache, …)
///   - the breadcrumb / nav-context broadcasts
///
/// This loader is the single source of truth for that shared state. Section pages call
/// <see cref="EnsureLoadedAsync"/> with their route's <c>Id</c> parameter on
/// <c>OnParametersSetAsync</c>; calls for the same id no-op, calls for a different id swap
/// the underlying connection + state cleanly.
///
/// Scoped service (one per Blazor circuit). Auto-clears on navigation away from
/// <c>/servers/{id}/*</c> - same pattern as the existing <see cref="ServerNavContextService"/>,
/// which this loader will subsume once the refactor lands fully.
/// </summary>
public sealed class ServerDetailLoader : IAsyncDisposable
{
    private readonly ApiClient _api;
    private readonly HubConnectionFactory _hubFactory;
    private readonly NavigationManager _nav;
    private readonly ILogger<ServerDetailLoader> _logger;

    private int? _currentId;
    private HubConnection? _hub;
    private CancellationTokenSource? _loadCts;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ServerDetailDto? Server { get; private set; }
    public List<ServerMetricDto> Metrics { get; private set; } = [];
    public DateTime? LastUpdated { get; private set; }
    public bool MetricsReceived { get; private set; }
    public bool InitialLoadCompleted { get; private set; }

    /// <summary>TC8R: latest agent-reported pipeline-toolchain capability (.NET SDK + git), from the
    /// heartbeat. Null until the first heartbeat arrives or for a pre-feature agent (unknown).</summary>
    public bool? PipelineRunnerAvailable { get; private set; }

    /// <summary>
    /// The shared "servers" SignalR connection. Exposed so the layout chrome can hand it to
    /// <c>AgentUpdateProgressCard</c> (which subscribes to the agent self-update events on the
    /// same connection - no double-subscribe on the server group). Null until the load completes.
    /// </summary>
    public HubConnection? Hub => _hub;

    /// <summary>Fired whenever <see cref="Server"/> / metric flags change. UI re-renders.</summary>
    public event Action? OnChanged;

    /// <summary>
    /// Fired when a task targeting the loaded server completes. Section pages subscribe to
    /// refresh their own state - e.g. ServerApacheSection refreshes config/logs on
    /// "Apache config" completion. Replaces the legacy <c>_apacheSection?.HandleTaskCompleted</c>
    /// fan-out that ServerDetail.razor used to do (item #1 polish).
    /// </summary>
    public event Func<TaskCompletedNotification, Task>? OnTaskCompleted;

    public ServerDetailLoader(ApiClient api, HubConnectionFactory hubFactory, NavigationManager nav, ILogger<ServerDetailLoader> logger)
    {
        _api = api;
        _hubFactory = hubFactory;
        _nav = nav;
        _logger = logger;
        _nav.LocationChanged += OnLocationChanged;
    }

    /// <summary>
    /// Loads (or no-ops for the same id) the server detail and joins its SignalR group. Safe
    /// to call from <c>OnParametersSetAsync</c> on every page render - the semaphore + id
    /// comparison protect against concurrent loads on the same id.
    /// </summary>
    public async Task EnsureLoadedAsync(int id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_currentId == id && Server is not null) return;

            // Switching servers: tear down the previous hub group + cancel any in-flight load
            // so an out-of-order response cannot overwrite the new server's data.
            await TeardownAsync().ConfigureAwait(false);

            _currentId = id;
            _loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _loadCts.Token;

            Server = await _api.Servers.GetServerDetailAsync(id, token).ConfigureAwait(false);
            Metrics = await LoadMetricsAsync(id, afterUtc: null, ct: token).ConfigureAwait(false);
            InitialLoadCompleted = true;
            if (Server is { MemoryTotalMb: > 0 })
            {
                MetricsReceived = true;
                LastUpdated = DateTime.Now;
            }
            OnChanged?.Invoke();

            if (Server is not null)
            {
                // Heartbeats enrich the page after its initial HTTP state is visible. SignalR
                // negotiation is best effort and must never hold every server section loader.
                _ = StartHubAsync(id, token);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Forces a re-fetch of the loaded server's detail (keeping the SignalR group intact) so a
    /// state-changing operation - service install/uninstall/lifecycle - reflects immediately instead
    /// of waiting for the next heartbeat. No-ops when nothing is loaded or the id no longer matches.
    /// </summary>
    public async Task RefreshAsync(int id, CancellationToken ct = default)
    {
        if (_currentId != id || Server is null) return;

        var fresh = await _api.Servers.GetServerDetailAsync(id, ct).ConfigureAwait(false);
        if (fresh is null || _currentId != id) return;

        Server = fresh;
        DateTime? afterUtc = Metrics.Count == 0 ? null : Metrics[^1].Timestamp;
        var newMetrics = await LoadMetricsAsync(id, afterUtc, ct).ConfigureAwait(false);
        if (newMetrics.Count > 0)
            Metrics.AddRange(newMetrics);
        TrimMetricWindow(DateTime.Now);
        LastUpdated = DateTime.Now;
        OnChanged?.Invoke();
    }

    private async Task StartHubAsync(int id, CancellationToken ct)
    {
        var hub = _hubFactory.Create("servers");

        hub.On<ServerHeartbeatDto>("Heartbeat", h =>
        {
            if (Server is null) return;
            var capabilities = ResolveSudoersCapabilities(Server, h);
            Server = Server with
            {
                CpuPercent = h.CpuPercent,
                MemoryUsedMb = h.MemoryUsedMb,
                MemoryTotalMb = h.MemoryTotalMb,
                DiskUsedGb = h.Disks.Sum(d => d.UsedGb),
                DiskTotalGb = h.Disks.Sum(d => d.TotalGb),
                Services = h.Services,
                Docker = h.Docker,
                Apache = h.Apache,
                Certbot = h.Certbot,
                Cron = h.Cron,
                Mail = h.Mail,
                Teamspeak = h.Teamspeak,
                Portsentry = h.Portsentry,
                Rkhunter = h.Rkhunter,
                AgentVersion = h.AgentVersion ?? Server.AgentVersion,
                AgentInstalledAt = h.AgentInstalledAt ?? Server.AgentInstalledAt,
                // Mirror the backend's per-heartbeat capability self-heal (ServerHeartbeatProcessor):
                // these flags are derived from the agent-reported sudoers drop-ins / docker probe and
                // self-heal both ways every beat. Without copying them onto the live record they stayed
                // frozen at the initial-load value, so a "package management not enabled" warning lingered
                // until a full reload even after a re-install with --enable-package-manage flipped it on.
                DockerAvailable = h.DockerAvailable,
                PackageManagementAvailable = capabilities.PackageManagement,
                MailSetupAvailable = capabilities.MailSetup,
                DeploymentTargetAvailable = capabilities.DeploymentTarget,
                InsecureTls = h.InsecureTls,
                StorageDiagnostics = h.StorageDiagnostics,
                // S-TECH-43: stamp the live heartbeat so "last seen" refreshes in real-time instead
                // of staying frozen at the value captured on the initial load (browser-local clock).
                LastHeartbeat = DateTime.Now,
                Status = ServerStatus.Online
            };
            PipelineRunnerAvailable = h.PipelineRunnerAvailable;
            AppendMetric(h);
            MetricsReceived = true;
            LastUpdated = DateTime.Now;
            OnChanged?.Invoke();
        });

        hub.On<int>("ServerOffline", serverId =>
        {
            if (Server is null || serverId != id) return;
            Server = Server with { Status = ServerStatus.Offline };
            OnChanged?.Invoke();
        });

        hub.On<TaskCompletedNotification>("TaskCompleted", async notification =>
        {
            if (notification.ServerId != id) return;
            var handlers = OnTaskCompleted?.GetInvocationList()
                .Cast<Func<TaskCompletedNotification, Task>>()
                .Select(handler => handler(notification))
                .ToArray() ?? [];
            await Task.WhenAll(handlers).ConfigureAwait(false);
        });

        // Group membership is per-connection and lost on auto-reconnect - re-join so server
        // updates/heartbeats keep flowing (the next heartbeat re-syncs the metrics).
        hub.RejoinOnReconnect(() => hub.InvokeAsync("JoinServerGroup", id));
        try
        {
            await hub.StartAsync(ct).ConfigureAwait(false);
            await hub.InvokeAsync("JoinServerGroup", id, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested || _currentId != id) return;
            var previous = Interlocked.Exchange(ref _hub, hub);
            if (previous is not null && !ReferenceEquals(previous, hub))
                await previous.DisposeAsync().ConfigureAwait(false);

            // Teardown may have raced the exchange. Keep no connection for a server that is no
            // longer current, otherwise transfer ownership to the loader and expose it to the UI.
            if (ct.IsCancellationRequested || _currentId != id)
                Interlocked.CompareExchange(ref _hub, null, hub);
            else
            {
                hub = null!;
                OnChanged?.Invoke();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Navigation cancelled a best-effort connection attempt.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ServerDetailLoader] Real-time connection unavailable for server {ServerId}",
                id);
        }
        finally
        {
            if (hub is not null)
            {
                try { await hub.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogDebug(ex, "[ServerDetailLoader] hub dispose failed"); }
            }
        }
    }

    internal static (bool? PackageManagement, bool MailSetup, bool? DeploymentTarget)
        ResolveSudoersCapabilities(ServerDetailDto server, ServerHeartbeatDto heartbeat)
    {
        var authoritative = heartbeat.SudoersInventoryAvailable == true
            || heartbeat.SudoersHashes.Count > 0;
        return authoritative
            ? (heartbeat.SudoersHashes.ContainsKey("aetheus-package"),
                heartbeat.SudoersHashes.ContainsKey("aetheus-mail"),
                heartbeat.SudoersHashes.ContainsKey("aetheus-deploy"))
            : (server.PackageManagementAvailable, server.MailSetupAvailable, server.DeploymentTargetAvailable);
    }

    /// <summary>Capability flags computed from the loaded server - sane defaults when not loaded.</summary>
    public bool HasDocker => Server is { Type: ServerType.Docker } ||
                             Server?.Docker is { Containers.Count: > 0 } ||
                             Server?.Docker is { Images.Count: > 0 } ||
                             Server?.Docker is { ComposeStacks.Count: > 0 } ||
                             HasService("docker");
    public bool HasApache => Server?.Apache.IsInstalled == true || HasService("apache2");
    public bool HasCertbot => Server?.Certbot.IsInstalled == true || HasService("certbot");
    public bool HasCron => Server?.Cron.IsInstalled == true || HasService("cron") || HasService("crond");
    public bool HasMail => Server?.Mail.IsInstalled == true || HasService("postfix") || HasService("dovecot");
    public bool HasTeamspeak => Server?.Teamspeak.IsInstalled == true;
    public bool HasPortsentry => Server?.Portsentry.IsInstalled == true || HasService("portsentry");
    public bool HasRkhunter => Server?.Rkhunter.IsInstalled == true || HasService("rkhunter");

    private bool HasService(string name) =>
        Server?.Services.Any(s => s.IsInstalled && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == true;

    private async Task<List<ServerMetricDto>> LoadMetricsAsync(
        int id,
        DateTime? afterUtc,
        CancellationToken ct)
    {
        try
        {
            return await _api.Monitoring.GetServerMetricsAsync(
                id,
                24,
                ct,
                afterUtc,
                take: 1_000).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "[ServerDetailLoader] Storage metric history could not be loaded for server {ServerId}", id);
            return [];
        }
    }

    private void AppendMetric(ServerHeartbeatDto heartbeat)
    {
        var storage = heartbeat.StorageDiagnostics;
        var now = storage.CollectedAtUtc.Year < 2000
            ? DateTime.Now
            : ToLocalClock(storage.CollectedAtUtc);
        Metrics.Add(new ServerMetricDto
        {
            ServerId = Server?.Id ?? 0,
            CpuPercent = heartbeat.CpuPercent,
            MemoryUsedMb = heartbeat.MemoryUsedMb,
            MemoryTotalMb = heartbeat.MemoryTotalMb,
            DiskUsedGb = heartbeat.Disks.Sum(d => d.UsedGb),
            DiskTotalGb = heartbeat.Disks.Sum(d => d.TotalGb),
            BuildCacheAvailable = storage.BuildCacheAvailable,
            DockerInventoryAvailable = storage.DockerInventoryAvailable,
            BuildCacheBytes = storage.BuildCacheBytes,
            BuildCacheReclaimableBytes = storage.BuildCacheReclaimableBytes,
            DockerImagesBytes = storage.DockerImagesBytes,
            DockerContainersBytes = storage.DockerContainersBytes,
            DockerVolumesBytes = storage.DockerVolumesBytes,
            AgentWorkDirectoryBytes = storage.AgentWorkDirectoryBytes,
            AgentInstallDirectoryBytes = storage.AgentInstallDirectoryBytes,
            NuGetCacheBytes = storage.NuGetCacheBytes,
            JournalBytes = storage.JournalBytes,
            StorageMaintenanceDryRun = storage.DryRun,
            DeploymentOnly = storage.DeploymentOnly,
            BuildActive = storage.BuildActive,
            LastBuildAttemptAtUtc = storage.LastBuildAttemptAtUtc,
            Timestamp = now
        });

        TrimMetricWindow(now);
    }

    internal static DateTime ToLocalClock(DateTime timestamp, TimeZoneInfo? timeZone = null)
    {
        if (timestamp.Kind == DateTimeKind.Local && timeZone is null)
            return timestamp;

        var utc = timestamp.Kind == DateTimeKind.Utc
            ? timestamp
            : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone ?? TimeZoneInfo.Local);
    }

    private void TrimMetricWindow(DateTime localNow)
    {
        var cutoff = localNow.AddHours(-24);
        Metrics.RemoveAll(metric => metric.Timestamp < cutoff);
        if (Metrics.Count > 1_000)
            Metrics.RemoveRange(0, Metrics.Count - 1_000);
    }

    /// <summary>
    /// Applies an edit (name / type / tags) from the layout chrome's inline edit panel onto the
    /// loaded <see cref="Server"/> and fires <see cref="OnChanged"/> so the header re-renders.
    /// No-ops when nothing is loaded.
    /// </summary>
    public void UpdateServerFields(string name, ServerType type, IReadOnlyList<string> tags)
    {
        if (Server is null) return;
        Server = Server with { Name = name, Type = type, Tags = [.. tags] };
        OnChanged?.Invoke();
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // Auto-clear when the user navigates away from /servers/{id}/* - symmetric with the
        // existing ServerNavContextService and avoids leaking the SignalR connection across
        // unrelated pages.
        var rel = _nav.ToBaseRelativePath(_nav.Uri);
        if (!rel.StartsWith("servers/", StringComparison.OrdinalIgnoreCase))
            _ = TeardownAsync();
    }

    private async Task TeardownAsync()
    {
        try { _loadCts?.Cancel(); }
        catch (ObjectDisposedException) { /* already disposed */ }
        _loadCts?.Dispose();
        _loadCts = null;
        var hub = Interlocked.Exchange(ref _hub, null);
        if (hub is not null)
        {
            try
            {
                if (_currentId.HasValue)
                    await hub.InvokeAsync("LeaveServerGroup", _currentId.Value).ConfigureAwait(false);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "[ServerDetailLoader] LeaveServerGroup failed"); }
            try { await hub.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "[ServerDetailLoader] hub dispose failed"); }
        }

        _currentId = null;
        Server = null;
        Metrics = [];
        LastUpdated = null;
        MetricsReceived = false;
        InitialLoadCompleted = false;
        OnChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _nav.LocationChanged -= OnLocationChanged;
        await TeardownAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
