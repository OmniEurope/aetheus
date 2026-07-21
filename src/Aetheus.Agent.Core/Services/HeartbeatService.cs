// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Collectors;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Shared.DTOs;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Services;

public sealed class HeartbeatService(
    IServerApiClient apiClient,
    IEnrollmentService enrollment,
    IMetricsCollector metricsCollector,
    IServiceCollector serviceCollector,
    IDockerCollector dockerCollector,
    IApacheCollector apacheCollector,
    ICertbotCollector certbotCollector,
    IMailCollector mailCollector,
    ITeamspeakCollector teamspeakCollector,
    IPortsentryCollector portsentryCollector,
    IRkhunterCollector rkhunterCollector,
    ISecurityUpdatesCollector securityUpdatesCollector,
    IFirewallCollector firewallCollector,
    ISudoersHashCollector sudoersHashCollector,
    IDockerStorageMaintenance dockerStorageMaintenance,
    IShellRunner shell,
    AgentState agentState,
    AgentRuntimeHealth runtimeHealth,
    TimeProvider timeProvider,
    IOptions<AetheusAgentOptions> options,
    ILogger<HeartbeatService> logger) : BackgroundService
{
    private readonly AetheusAgentOptions _options = options.Value;
    private readonly TaskCompletionSource _loopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task LoopStarted => _loopStarted.Task;
    private readonly TimeSpan _collectionTimeout = TimeSpan.FromSeconds(
        Math.Clamp(options.Value.HeartbeatCollectionTimeoutSeconds, 1, 60));
    private static readonly string AgentVersion =
        typeof(HeartbeatService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    // Resolved once at startup - the install timestamp can't change while the agent is
    // running; rereading the marker on every tick would add an FS round-trip per heartbeat.
    private readonly DateTime? _agentInstalledAt = InstallInfoReader.Read(options.Value.WorkDirectory);

    // Every heartbeat used to fork ~15 shell processes, most of them probing inventory that
    // changes rarely (docker images/networks/volumes, certbot certs, sudoers hashes...). The
    // slow collectors are TTL-cached; CPU/RAM metrics and the service list stay per-beat.
    // Docker gets a shorter TTL so container up/down still surfaces quickly.
    private static readonly TimeSpan DockerInventoryTtl = AgentRuntimeDefaults.DockerInventoryTtl;
    private static readonly TimeSpan SlowInventoryTtl = AgentRuntimeDefaults.SlowInventoryTtl;

    private readonly CachedCollector<List<ServiceInfoDto>> _servicesCache = new(
        TimeSpan.Zero, serviceCollector.CollectAsync, timeProvider, static () => [], logger, "services");
    private readonly CachedCollector<DockerDataDto> _dockerCache = new(
        DockerInventoryTtl, dockerCollector.CollectAllAsync, timeProvider, static () => new DockerDataDto(), logger, "docker");
    private readonly CachedCollector<ApacheDataDto> _apacheCache = new(
        SlowInventoryTtl, apacheCollector.CollectAsync, timeProvider, static () => new ApacheDataDto(), logger, "apache");
    private readonly CachedCollector<CertbotDataDto> _certbotCache = new(
        SlowInventoryTtl, certbotCollector.CollectAsync, timeProvider, static () => new CertbotDataDto(), logger, "certbot");
    private readonly CachedCollector<MailDataDto> _mailCache = new(
        SlowInventoryTtl, mailCollector.CollectAsync, timeProvider, static () => new MailDataDto(), logger, "mail");
    private readonly CachedCollector<TeamspeakDataDto> _teamspeakCache = new(
        SlowInventoryTtl, teamspeakCollector.CollectAsync, timeProvider, static () => new TeamspeakDataDto(), logger, "teamspeak");
    private readonly CachedCollector<PortsentryDataDto> _portsentryCache = new(
        SlowInventoryTtl, portsentryCollector.CollectAsync, timeProvider, static () => new PortsentryDataDto(), logger, "portsentry");
    private readonly CachedCollector<RkhunterDataDto> _rkhunterCache = new(
        SlowInventoryTtl, rkhunterCollector.CollectAsync, timeProvider, static () => new RkhunterDataDto(), logger, "rkhunter");
    private readonly CachedCollector<SecurityUpdatesDataDto> _securityUpdatesCache = new(
        SlowInventoryTtl, securityUpdatesCollector.CollectAsync, timeProvider, static () => new SecurityUpdatesDataDto(), logger, "security updates");
    private readonly CachedCollector<FirewallDataDto> _firewallCache = new(
        SlowInventoryTtl, firewallCollector.CollectAsync, timeProvider, static () => new FirewallDataDto(), logger, "firewall");
    private readonly CachedCollector<Dictionary<string, string>> _sudoersCache = new(
        SlowInventoryTtl, sudoersHashCollector.CollectAsync, timeProvider, static () => new Dictionary<string, string>(), logger, "sudoers");
    private readonly CachedCollector<bool> _dockerAvailableCache = new(
        SlowInventoryTtl, ct => DockerProbe.IsAvailableAsync(shell, ct), timeProvider, static () => false, logger, "docker availability");
    private readonly CachedCollector<bool> _pipelineRunnerAvailableCache = new(
        SlowInventoryTtl, ct => PipelineRunnerProbe.IsAvailableAsync(shell, ct), timeProvider, static () => false, logger, "pipeline runner");
    private readonly CachedCollector<List<string>> _capDiagnosticsCache = new(
        SlowInventoryTtl, ct => CapabilityDiagnosticsProbe.CollectAsync(shell, ct), timeProvider, static () => [], logger, "capability diagnostics");
    private readonly CachedCollector<StorageDiagnosticsDto> _storageDiagnosticsCache = new(
        SlowInventoryTtl, dockerStorageMaintenance.CollectDiagnosticsAsync, timeProvider,
        static () => new StorageDiagnosticsDto(), logger, "storage diagnostics");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!enrollment.IsEnrolled && !stoppingToken.IsCancellationRequested)
            await Task.Delay(AgentRuntimeDefaults.StartupRetryDelay, timeProvider, stoppingToken).ConfigureAwait(false);

        runtimeHealth.MarkHeartbeatProgress();
        logger.LogInformation("Heartbeat service started (interval: {Interval}s)", _options.HeartbeatIntervalSeconds);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds),
            timeProvider);
        _loopStarted.TrySetResult();

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            runtimeHealth.MarkHeartbeatProgress();
            try
            {
                await CollectAndSendHeartbeatAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heartbeat failed, will retry next interval");
            }
        }
    }

    // E-3: one iteration's work, extracted so tests can drive it deterministically
    // without a PeriodicTimer or a real Task.Delay sleep.
    internal async Task CollectAndSendHeartbeatAsync(CancellationToken ct)
    {
        var heartbeat = metricsCollector.Collect();

        using var collectorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var collectorToken = collectorCts.Token;
        var servicesTask = _servicesCache.GetAsync(collectorToken);
        var dockerTask = _dockerCache.GetAsync(collectorToken);
        var apacheTask = _apacheCache.GetAsync(collectorToken);
        var certbotTask = _certbotCache.GetAsync(collectorToken);
        var mailTask = _mailCache.GetAsync(collectorToken);
        var teamspeakTask = _teamspeakCache.GetAsync(collectorToken);
        var portsentryTask = _portsentryCache.GetAsync(collectorToken);
        var rkhunterTask = _rkhunterCache.GetAsync(collectorToken);
        var securityUpdatesTask = _securityUpdatesCache.GetAsync(collectorToken);
        var firewallTask = _firewallCache.GetAsync(collectorToken);
        var sudoersTask = _sudoersCache.GetAsync(collectorToken);
        var dockerAvailableTask = _dockerAvailableCache.GetAsync(collectorToken);
        var pipelineRunnerAvailableTask = _pipelineRunnerAvailableCache.GetAsync(collectorToken);
        var capDiagnosticsTask = _capDiagnosticsCache.GetAsync(collectorToken);
        var storageDiagnosticsTask = _storageDiagnosticsCache.GetAsync(collectorToken);
        var allCollectors = Task.WhenAll(
            servicesTask, dockerTask, apacheTask, certbotTask, mailTask, teamspeakTask,
            portsentryTask, rkhunterTask, securityUpdatesTask, firewallTask, sudoersTask,
            dockerAvailableTask, pipelineRunnerAvailableTask, capDiagnosticsTask, storageDiagnosticsTask);

        var timeoutTask = Task.Delay(_collectionTimeout, timeProvider, ct);
        var collectorsCompleted = await Task.WhenAny(allCollectors, timeoutTask).ConfigureAwait(false) == allCollectors;
        if (collectorsCompleted)
        {
            await allCollectors.ConfigureAwait(false);
        }
        else
        {
            await collectorCts.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            logger.LogWarning(
                "Heartbeat inventory collection exceeded {TimeoutSeconds}s; sending cached/degraded data",
                _collectionTimeout.TotalSeconds);
            _ = ObserveLateCollectorsAsync(allCollectors);
        }

        var capabilityDiagnostics = CompletedValueOr(
            capDiagnosticsTask, _capDiagnosticsCache.LastValueOr(new List<string>()));
        if (!collectorsCompleted)
        {
            capabilityDiagnostics = [.. capabilityDiagnostics, "Heartbeat inventory collection timed out; cached or partial data reported"];
        }
        var quarantinedCollectors = GetQuarantinedCollectorNames();
        if (quarantinedCollectors.Count > 0)
        {
            capabilityDiagnostics =
            [
                .. capabilityDiagnostics,
                $"Heartbeat collectors quarantined: {string.Join(", ", quarantinedCollectors)}"
            ];
        }

        heartbeat = heartbeat with
        {
            AgentVersion = AgentVersion,
            AgentInstalledAt = _agentInstalledAt,
            DockerAvailable = CompletedValueOr(dockerAvailableTask, _dockerAvailableCache.LastValueOr(false)),
            PipelineRunnerAvailable = !_pipelineRunnerAvailableCache.IsCollectionInFlight
                && pipelineRunnerAvailableTask.Status == TaskStatus.RanToCompletion
                ? pipelineRunnerAvailableTask.Result
                : null,
            // S-DES-23: report the dev-only TLS-bypass mode so the UI can flag it on the server card.
            InsecureTls = _options.AllowInsecureCerts,
            Services = CompletedValueOr(servicesTask, _servicesCache.LastValueOr([])),
            Docker = CompletedValueOr(dockerTask, _dockerCache.LastValueOr(new DockerDataDto())),
            Apache = CompletedValueOr(apacheTask, _apacheCache.LastValueOr(new ApacheDataDto())),
            Certbot = CompletedValueOr(certbotTask, _certbotCache.LastValueOr(new CertbotDataDto())),
            Mail = CompletedValueOr(mailTask, _mailCache.LastValueOr(new MailDataDto())),
            Teamspeak = CompletedValueOr(teamspeakTask, _teamspeakCache.LastValueOr(new TeamspeakDataDto())),
            Portsentry = CompletedValueOr(portsentryTask, _portsentryCache.LastValueOr(new PortsentryDataDto())),
            Rkhunter = CompletedValueOr(rkhunterTask, _rkhunterCache.LastValueOr(new RkhunterDataDto())),
            SecurityUpdates = CompletedValueOr(
                securityUpdatesTask, _securityUpdatesCache.LastValueOr(new SecurityUpdatesDataDto())),
            Firewall = CompletedValueOr(firewallTask, _firewallCache.LastValueOr(new FirewallDataDto())),
            SudoersHashes = CompletedValueOr(
                sudoersTask, _sudoersCache.LastValueOr(new Dictionary<string, string>())),
            SudoersInventoryAvailable = _sudoersCache.HasValue,
            CapabilityDiagnostics = capabilityDiagnostics,
            StorageDiagnostics = CompletedValueOr(
                storageDiagnosticsTask, _storageDiagnosticsCache.LastValueOr(new StorageDiagnosticsDto()))
        };

        var response = await apiClient.SendHeartbeatAsync(agentState.ServerId!.Value, heartbeat, ct).ConfigureAwait(false);
        await ApplyTokenRenewalAsync(response, ct).ConfigureAwait(false);

        logger.LogDebug("Heartbeat sent - CPU: {Cpu}%, RAM: {Ram}/{Total} MB",
            heartbeat.CpuPercent, heartbeat.MemoryUsedMb, heartbeat.MemoryTotalMb);
    }

    private static T CompletedValueOr<T>(Task<T> task, T fallback) =>
        task.Status == TaskStatus.RanToCompletion ? task.Result : fallback;

    private List<string> GetQuarantinedCollectorNames()
    {
        (string Name, bool InFlight)[] collectors =
        [
            ("services", _servicesCache.IsCollectionInFlight),
            ("docker", _dockerCache.IsCollectionInFlight),
            ("apache", _apacheCache.IsCollectionInFlight),
            ("certbot", _certbotCache.IsCollectionInFlight),
            ("mail", _mailCache.IsCollectionInFlight),
            ("teamspeak", _teamspeakCache.IsCollectionInFlight),
            ("portsentry", _portsentryCache.IsCollectionInFlight),
            ("rkhunter", _rkhunterCache.IsCollectionInFlight),
            ("security updates", _securityUpdatesCache.IsCollectionInFlight),
            ("firewall", _firewallCache.IsCollectionInFlight),
            ("sudoers", _sudoersCache.IsCollectionInFlight),
            ("docker availability", _dockerAvailableCache.IsCollectionInFlight),
            ("pipeline runner", _pipelineRunnerAvailableCache.IsCollectionInFlight),
            ("capability diagnostics", _capDiagnosticsCache.IsCollectionInFlight),
            ("storage diagnostics", _storageDiagnosticsCache.IsCollectionInFlight)
        ];
        return collectors.Where(collector => collector.InFlight).Select(collector => collector.Name).ToList();
    }

    private async Task ObserveLateCollectorsAsync(Task collectors)
    {
        try
        {
            await collectors.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected after the collection budget expires.
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Late heartbeat collector failed after degraded heartbeat was sent");
        }
    }

    private async Task ApplyTokenRenewalAsync(ServerHeartbeatResponseDto? response, CancellationToken ct)
    {
        if (response is null || string.IsNullOrEmpty(response.RenewedToken)) return;
        // Persist FIRST; only swap the in-memory token once the new one is safely
        // on disk. If the persist throws, the old token is still valid until its
        // own server-side expiry (the server does not revoke it) - fail-safe.
        await enrollment.PersistRenewedTokenAsync(response.RenewedToken, ct).ConfigureAwait(false);
        agentState.BearerToken = response.RenewedToken;
        agentState.TokenExpiresAt = response.RenewedTokenExpiresAtUtc;
        apiClient.SetBearerToken(response.RenewedToken);
        logger.LogInformation("Agent Bearer token auto-renewed (expires {ExpiresAt:u})",
            response.RenewedTokenExpiresAtUtc);
    }

    // TTL cache plus a single-flight quarantine. A collector that ignores cancellation keeps
    // its one in-flight call, while later heartbeats immediately use cached/degraded data.
    private sealed class CachedCollector<T>(
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> collect,
        TimeProvider time,
        Func<T> fallback,
        ILogger logger,
        string name)
    {
        private readonly object _sync = new();
        private T? _value;
        private DateTimeOffset? _refreshedAt;
        private Task<T>? _inFlight;

        public Task<T> GetAsync(CancellationToken ct)
        {
            lock (_sync)
            {
                var now = time.GetUtcNow();
                if (_refreshedAt is not null && now - _refreshedAt < ttl && _value is not null)
                    return Task.FromResult(_value);

                if (_inFlight is not null)
                    return Task.FromResult(_value is null ? fallback() : _value);

                _inFlight = CollectAndCacheAsync(ct);
                return _inFlight;
            }
        }

        public T LastValueOr(T fallbackValue)
        {
            lock (_sync)
                return _refreshedAt is null || _value is null ? fallbackValue : _value;
        }

        public bool IsCollectionInFlight
        {
            get
            {
                lock (_sync)
                    return _inFlight is not null;
            }
        }

        public bool HasValue
        {
            get
            {
                lock (_sync)
                    return _refreshedAt is not null;
            }
        }

        private async Task<T> CollectAndCacheAsync(CancellationToken ct)
        {
            await Task.Yield();
            try
            {
                var collected = await collect(ct).ConfigureAwait(false);
                lock (_sync)
                {
                    _value = collected;
                    _refreshedAt = time.GetUtcNow();
                }
                return collected;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return LastValueOr(fallback());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heartbeat collector {Collector} failed; using cached/degraded data", name);
                return LastValueOr(fallback());
            }
            finally
            {
                lock (_sync)
                    _inFlight = null;
            }
        }
    }
}
