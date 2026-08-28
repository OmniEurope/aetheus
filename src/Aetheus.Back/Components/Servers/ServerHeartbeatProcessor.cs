// SPDX-License-Identifier: EUPL-1.2
using System.Collections.Concurrent;
using System.Text.Json;
using Aetheus.Back.Components.AgentUpdate;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Shared.Helpers;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Servers;

/// <summary>
/// Heartbeat ingestion: persists the agent-reported telemetry (metrics, services, module state),
/// broadcasts the live update, raises security alerts, and serves heartbeat history. Extracted from
/// the former <c>ServerService.HeartbeatUpdates.cs</c> / <c>HeartbeatHistory.cs</c> partials into a
/// real collaborator that <see cref="ServerService"/> delegates to.
/// </summary>
internal sealed class ServerHeartbeatProcessor(
    IServerRepository repo,
    IServerHeartbeatRepository heartbeatRepo,
    IHubContext<ServerHub> serverHub,
    IHubContext<AlertHub> alertHub,
    TimeProvider timeProvider,
    IDbTransactionScope transaction,
    IAgentUpdateConfirmationService? updateConfirmation,
    ILogger<ServerHeartbeatProcessor> logger)
{
    private static readonly ConcurrentDictionary<int, DateTime> DeploymentBuildAlerts = new();
    private static readonly TimeSpan DeploymentBuildAlertCooldown = TimeSpan.FromMinutes(15);

    public async Task ProcessHeartbeatAsync(int serverId, ServerHeartbeatDto heartbeat, CancellationToken ct = default)
    {
        var server = await repo.FindServerAsync(serverId, ct).ConfigureAwait(false);
        if (server is null) return;

        // The authenticated heartbeat itself proves that the agent is alive. Persist presence before
        // optional inventory sections so one malformed/duplicated collector payload cannot roll back
        // LastHeartbeat, mark the runner offline, and prevent the deployment that contains its fix.
        UpdateServerPresence(server);
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        var (previousBlockedCount, previousWarningCount) = await heartbeatRepo.GetSecurityCountersAsync(serverId, ct).ConfigureAwait(false);

        // Atomicity (audit-360 lot F): the per-section Replace* repo calls each run an
        // ExecuteDeleteAsync OUTSIDE the final SaveChanges transaction, so a mid-sequence failure
        // used to leave partial heartbeat state. Wrap the whole delete-then-insert-then-save
        // sequence in one transaction. The InMemory unit-test provider has no transactions, so
        // run the same body without one there (mirrors ServerRepository.RemoveServerAsync).
        if (transaction.IsRelational)
            await transaction.ExecuteInTransactionAsync(
                () => PersistHeartbeatAsync(server, serverId, heartbeat, ct), ct).ConfigureAwait(false);
        else
            await PersistHeartbeatAsync(server, serverId, heartbeat, ct).ConfigureAwait(false);

        if (updateConfirmation is not null)
            await updateConfirmation.ProcessHeartbeatAsync(serverId, heartbeat, ct).ConfigureAwait(false);
        await BroadcastHeartbeatAsync(serverId, heartbeat, ct).ConfigureAwait(false);
        await CheckSecurityAlertsAsync(server.Name, serverId, heartbeat, previousBlockedCount, previousWarningCount, ct).ConfigureAwait(false);
    }

    private async Task PersistHeartbeatAsync(Server server, int serverId, ServerHeartbeatDto heartbeat, CancellationToken ct)
    {
        ApplyAgentIdentity(server, serverId, heartbeat);
        ServerHeartbeatCapabilityProjector.Apply(server, heartbeat);
        ApplyCapabilityDiagnostics(server, serverId, heartbeat);
        await PersistHeartbeatInventoriesAsync(serverId, heartbeat, ct).ConfigureAwait(false);
        await CheckSudoersDriftAsync(server, heartbeat, ct).ConfigureAwait(false);
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private void ApplyAgentIdentity(Server server, int serverId, ServerHeartbeatDto heartbeat)
    {
        if (server.OsType == OsType.Unknown)
            server.OsType = OsTypeHelper.FromDescription(server.OsDescription);
        if (!string.IsNullOrWhiteSpace(heartbeat.AgentVersion) && server.AgentVersion != heartbeat.AgentVersion)
            server.AgentVersion = heartbeat.AgentVersion;
        if (!string.IsNullOrWhiteSpace(heartbeat.AgentSessionId)
            && server.AgentSessionId != heartbeat.AgentSessionId)
        {
            logger.LogInformation(
                "Server {ServerId} agent session changed from {PreviousSession} to {CurrentSession}",
                serverId,
                server.AgentSessionId ?? "legacy",
                heartbeat.AgentSessionId);
            server.AgentSessionId = heartbeat.AgentSessionId;
        }
        // Persist the agent install timestamp only when it actually moves forward - the agent
        // resolves it once at startup, so an unchanged value just races to the same write.
        if (heartbeat.AgentInstalledAt is { } installedAt && server.AgentInstalledAt != installedAt)
            server.AgentInstalledAt = installedAt;
    }

    private void ApplyCapabilityDiagnostics(Server server, int serverId, ServerHeartbeatDto heartbeat)
    {
        if (heartbeat.CapabilityDiagnostics.Count > 0)
        {
            logger.LogWarning("Server {ServerId} capability diagnostics: {Diagnostics}",
                serverId, string.Join("; ", heartbeat.CapabilityDiagnostics));
            var diagnosticsJson = JsonSerializer.Serialize(heartbeat.CapabilityDiagnostics);
            if (server.CapabilityDiagnosticsJson != diagnosticsJson)
                server.CapabilityDiagnosticsJson = diagnosticsJson;
        }
        else if (server.CapabilityDiagnosticsJson is not null)
        {
            server.CapabilityDiagnosticsJson = null;
        }
        var scannerCapabilitiesJson = heartbeat.ScannerCapabilities.Count == 0
            ? null
            : JsonSerializer.Serialize(heartbeat.ScannerCapabilities);
        if (server.ScannerCapabilitiesJson != scannerCapabilitiesJson)
            server.ScannerCapabilitiesJson = scannerCapabilitiesJson;
    }

    private async Task PersistHeartbeatInventoriesAsync(
        int serverId,
        ServerHeartbeatDto heartbeat,
        CancellationToken ct)
    {
        await StoreMetricAsync(serverId, heartbeat, ct).ConfigureAwait(false);
        await UpdateServicesAsync(serverId, heartbeat.Services, ct).ConfigureAwait(false);
        await UpdateDockerDataAsync(serverId, heartbeat.Docker, ct).ConfigureAwait(false);
        await UpdateApacheDataAsync(serverId, heartbeat.Apache, ct).ConfigureAwait(false);
        await UpdateCertbotDataAsync(serverId, heartbeat.Certbot, ct).ConfigureAwait(false);
        await UpdateMailDataAsync(serverId, heartbeat.Mail, ct).ConfigureAwait(false);
        await UpdateTeamspeakDataAsync(serverId, heartbeat.Teamspeak, ct).ConfigureAwait(false);
        await UpdatePortsentryDataAsync(serverId, heartbeat.Portsentry, ct).ConfigureAwait(false);
        await UpdateRkhunterDataAsync(serverId, heartbeat.Rkhunter, ct).ConfigureAwait(false);
        await UpdateSecurityUpdatesDataAsync(serverId, heartbeat.SecurityUpdates, ct).ConfigureAwait(false);
        await UpdateFirewallDataAsync(serverId, heartbeat.Firewall, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// S-TECH-15: compares the agent-reported sudoers hashes against the stored baseline.
    /// First report captures the baseline silently; a later hash that differs for the same
    /// file raises a Sec-Audit alert (the sudoers grant was tampered with out-of-band).
    /// </summary>
    private async Task CheckSudoersDriftAsync(Server server, ServerHeartbeatDto heartbeat, CancellationToken ct)
    {
        if (heartbeat.SudoersHashes.Count == 0) return;

        var reported = heartbeat.SudoersHashes;

        if (string.IsNullOrEmpty(server.SudoersBaseline))
        {
            // First-ever report: capture the baseline, no alert.
            server.SudoersBaseline = JsonSerializer.Serialize(reported);
            return;
        }

        Dictionary<string, string>? baseline;
        try
        {
            baseline = JsonSerializer.Deserialize<Dictionary<string, string>>(server.SudoersBaseline);
        }
        catch (JsonException)
        {
            baseline = null;
        }
        if (baseline is null) { server.SudoersBaseline = JsonSerializer.Serialize(reported); return; }

        var drifted = reported
            .Where(kv => baseline.TryGetValue(kv.Key, out var baseHash) && !string.Equals(baseHash, kv.Value, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToList();

        if (drifted.Count == 0) return;

        await alertHub.Clients.Group(HubGroups.Alerts).SendAsync("AlertTriggered", new AlertTriggeredDto
        {
            RuleName = "Sec-Audit",
            ServerId = server.Id,
            ServerName = server.Name,
            Metric = "SudoersDrift",
            Severity = "Critical",
            Message = $"Sudoers drop-in changed out-of-band: {string.Join(", ", drifted)}. "
                      + "Verify the change was authorized; the install baseline no longer matches.",
            TriggeredAt = timeProvider.GetUtcNow().UtcDateTime
        }, ct).ConfigureAwait(false);
    }

    private void UpdateServerPresence(Server server)
    {
        server.Status = ServerStatus.Online;
        server.LastHeartbeat = timeProvider.GetUtcNow().UtcDateTime;
    }

    private async Task StoreMetricAsync(int serverId, ServerHeartbeatDto heartbeat, CancellationToken ct)
    {
        var diskTotal = heartbeat.Disks.Sum(d => d.TotalGb);
        var diskUsed = heartbeat.Disks.Sum(d => d.UsedGb);
        var storage = heartbeat.StorageDiagnostics;
        await heartbeatRepo.AddMetricAsync(new ServerMetric
        {
            ServerId = serverId,
            CpuPercent = heartbeat.CpuPercent,
            MemoryUsedMb = heartbeat.MemoryUsedMb,
            MemoryTotalMb = heartbeat.MemoryTotalMb,
            DiskUsedGb = diskUsed,
            DiskTotalGb = diskTotal,
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
            Timestamp = timeProvider.GetUtcNow().UtcDateTime
        }, ct).ConfigureAwait(false);
    }

    private async Task UpdateServicesAsync(int serverId, List<ServiceInfoDto> services, CancellationToken ct)
    {
        await heartbeatRepo.ReplaceServicesAsync(serverId, services.Select(svc => new ServiceInfo
        {
            ServerId = serverId,
            Name = svc.Name,
            Type = svc.Type,
            Status = svc.Status,
            IsRunning = svc.IsRunning,
            IsManageable = ServerDataMapper.ResolveManageable(svc.Name, svc.IsManageable)
        }).ToList(), ct).ConfigureAwait(false);
    }

    private async Task BroadcastHeartbeatAsync(int serverId, ServerHeartbeatDto heartbeat, CancellationToken ct)
    {
        var supplemented = heartbeat with { Services = ServerDataMapper.SupplementWithWellKnown(heartbeat.Services) };
        await serverHub.Clients.Groups([HubGroups.AllServers, HubGroups.Server(serverId)]).SendAsync("ServerHeartbeat", serverId, supplemented, ct).ConfigureAwait(false);
        await serverHub.Clients.Group($"server-{serverId}").SendAsync("Heartbeat", supplemented, ct).ConfigureAwait(false);
    }

    private async Task CheckSecurityAlertsAsync(string serverName, int serverId, ServerHeartbeatDto heartbeat, int previousBlockedCount, int previousWarningCount, CancellationToken ct)
    {
        if (heartbeat.Portsentry.BlockedCount > previousBlockedCount)
        {
            var newBlocks = heartbeat.Portsentry.BlockedCount - previousBlockedCount;
            await alertHub.Clients.Group(HubGroups.Alerts).SendAsync("AlertTriggered", new AlertTriggeredDto
            {
                RuleName = "PortSentry",
                ServerId = serverId,
                ServerName = serverName,
                Metric = "BlockedIps",
                Severity = "Warning",
                Message = $"{newBlocks} new IP(s) blocked by PortSentry",
                TriggeredAt = timeProvider.GetUtcNow().UtcDateTime
            }, ct).ConfigureAwait(false);
        }

        if (heartbeat.Rkhunter.WarningCount > previousWarningCount)
        {
            var newWarnings = heartbeat.Rkhunter.WarningCount - previousWarningCount;
            await alertHub.Clients.Group(HubGroups.Alerts).SendAsync("AlertTriggered", new AlertTriggeredDto
            {
                RuleName = "RKHunter",
                ServerId = serverId,
                ServerName = serverName,
                Metric = "RkhunterWarnings",
                Severity = heartbeat.Rkhunter.WarningCount > 5 ? "Critical" : "Warning",
                Message = $"{newWarnings} new warning(s) detected by RKHunter",
                TriggeredAt = timeProvider.GetUtcNow().UtcDateTime
            }, ct).ConfigureAwait(false);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var storage = heartbeat.StorageDiagnostics;
        var recentBuildAttempt = storage.LastBuildAttemptAtUtc is { } buildAttempt
            && buildAttempt >= now.AddMinutes(-5)
            && buildAttempt <= now.AddMinutes(5);
        if (storage.DeploymentOnly && (storage.BuildActive || recentBuildAttempt)
            && (!DeploymentBuildAlerts.TryGetValue(serverId, out var lastAlert)
                || now - lastAlert >= DeploymentBuildAlertCooldown))
        {
            DeploymentBuildAlerts[serverId] = now;
            logger.LogCritical(
                "Docker build observed on deployment-only server {ServerId} ({ServerName})",
                serverId, serverName);
            await alertHub.Clients.Group(HubGroups.Alerts).SendAsync("AlertTriggered", new AlertTriggeredDto
            {
                RuleName = "StoragePolicy",
                ServerId = serverId,
                ServerName = serverName,
                Metric = "BuildOnDeploymentTarget",
                Severity = "Critical",
                Message = "A Docker build attempt was observed on a server configured as deployment-only.",
                TriggeredAt = now
            }, ct).ConfigureAwait(false);
        }
    }

    private async Task UpdateDockerDataAsync(int serverId, DockerDataDto docker, CancellationToken ct)
    {
        await heartbeatRepo.ReplaceDockerContainersAsync(serverId, docker.Containers.Select(c => new DockerContainer
        {
            ServerId = serverId,
            ContainerId = c.ContainerId,
            Name = c.Name,
            Image = c.Image,
            State = c.State,
            Status = c.Status,
            Ports = c.Ports,
            Created = c.Created,
            CpuPercent = c.CpuPercent,
            MemoryUsageMb = c.MemoryUsageMb,
            MemoryLimitMb = c.MemoryLimitMb
        }).ToList(), ct).ConfigureAwait(false);

        // Docker lists one row per repository/tag, not per image. Several tags can therefore carry
        // the same immutable ImageId, while the persisted model is one image per server/ImageId.
        // Keep a tagged row when present so the dashboard remains useful and the unique index holds.
        var images = docker.Images
            .GroupBy(img => img.ImageId, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(img => string.Equals(img.Repository, "<none>", StringComparison.OrdinalIgnoreCase))
                .First())
            .Select(img => new DockerImage
            {
                ServerId = serverId,
                ImageId = img.ImageId,
                Repository = img.Repository,
                Tag = img.Tag,
                Size = img.Size,
                Created = img.Created
            }).ToList();
        await heartbeatRepo.ReplaceDockerImagesAsync(serverId, images, ct).ConfigureAwait(false);

        await heartbeatRepo.ReplaceDockerComposeStacksAsync(serverId, docker.ComposeStacks.Select(stack => new DockerComposeStack
        {
            ServerId = serverId,
            Name = stack.Name,
            Status = stack.Status,
            ConfigFile = stack.ConfigFile,
            RunningCount = stack.RunningCount,
            TotalCount = stack.TotalCount
        }).ToList(), ct).ConfigureAwait(false);

        await heartbeatRepo.ReplaceDockerNetworksAsync(serverId, docker.Networks.Select(net => new DockerNetwork
        {
            ServerId = serverId,
            NetworkId = net.NetworkId,
            Name = net.Name,
            Driver = net.Driver,
            Scope = net.Scope
        }).ToList(), ct).ConfigureAwait(false);

        await heartbeatRepo.ReplaceDockerVolumesAsync(serverId, docker.Volumes.Select(vol => new DockerVolume
        {
            ServerId = serverId,
            Name = vol.Name,
            Driver = vol.Driver,
            Mountpoint = vol.Mountpoint
        }).ToList(), ct).ConfigureAwait(false);
    }

    private async Task UpdateApacheDataAsync(int serverId, ApacheDataDto apache, CancellationToken ct)
    {
        ApacheState? apacheState = apache.IsInstalled ? new ApacheState
        {
            ServerId = serverId,
            Version = apache.Version,
            IsRunning = apache.IsRunning,
            Pid = apache.Pid,
            ConfigRoot = apache.ConfigRoot,
            CollectionDegraded = apache.CollectionDegraded,
            CollectionDiagnostics = apache.CollectionDiagnostics
        } : null;

        var apacheModules = apache.Modules.Select(m => new ApacheModule
        {
            ServerId = serverId,
            Name = m.Name,
            Type = m.Type,
            IsEnabled = m.IsEnabled
        }).ToList();

        var apacheVhosts = apache.VirtualHosts.Select(v => new ApacheVirtualHost
        {
            ServerId = serverId,
            ServerName = v.ServerName,
            Port = v.Port,
            DocumentRoot = v.DocumentRoot,
            ConfigFile = v.ConfigFile,
            IsEnabled = v.IsEnabled
        }).ToList();

        await heartbeatRepo.ReplaceApacheDataAsync(serverId, apacheState, apacheModules, apacheVhosts, ct).ConfigureAwait(false);
    }

    private async Task UpdateCertbotDataAsync(int serverId, CertbotDataDto certbot, CancellationToken ct)
    {
        var certbotCerts = certbot.Certificates.Select(c => new CertbotCertificate
        {
            ServerId = serverId,
            Name = c.Name,
            Domains = JsonSerializer.Serialize(c.Domains),
            ExpiryDate = c.ExpiryDate,
            CertPath = c.CertPath,
            KeyPath = c.KeyPath
        }).ToList();

        await heartbeatRepo.ReplaceCertbotCertificatesAsync(serverId, certbotCerts, ct).ConfigureAwait(false);
    }

    private async Task UpdateMailDataAsync(int serverId, MailDataDto mail, CancellationToken ct)
    {
        MailState? mailState = mail.IsInstalled ? new MailState
        {
            ServerId = serverId,
            PostfixVersion = mail.PostfixVersion,
            DovecotVersion = mail.DovecotVersion,
            IsPostfixRunning = mail.IsPostfixRunning,
            IsDovecotRunning = mail.IsDovecotRunning,
            QueueSize = mail.QueueSize
        } : null;

        await heartbeatRepo.ReplaceMailDataAsync(serverId, mailState, ct).ConfigureAwait(false);
    }

    private async Task UpdateTeamspeakDataAsync(int serverId, TeamspeakDataDto teamspeak, CancellationToken ct)
    {
        TeamspeakState? state = teamspeak.IsInstalled ? new TeamspeakState
        {
            ServerId = serverId,
            Version = teamspeak.Version,
            Platform = teamspeak.Platform,
            IsRunning = teamspeak.IsRunning,
            ServerName = teamspeak.ServerName,
            VoicePort = teamspeak.VoicePort,
            QueryPort = teamspeak.QueryPort,
            MaxClients = teamspeak.MaxClients,
            OnlineClients = teamspeak.OnlineClients,
            ChannelCount = teamspeak.ChannelCount,
            UptimeSeconds = teamspeak.UptimeSeconds
        } : null;

        var channels = teamspeak.Channels.Select(c => new TeamspeakChannel
        {
            ServerId = serverId,
            ChannelId = c.Id,
            Name = c.Name,
            ParentId = c.ParentId,
            Order = c.Order,
            TotalClients = c.TotalClients,
            MaxClients = c.MaxClients,
            IsDefault = c.IsDefault,
            HasPassword = c.HasPassword,
            IsPermanent = c.IsPermanent
        }).ToList();

        var channelNames = teamspeak.Channels.ToDictionary(channel => channel.Id, channel => channel.Name);
        var clients = teamspeak.Clients.Select(client => new TeamspeakClient
        {
            ServerId = serverId,
            ClientId = client.ClientId,
            UniqueId = client.UniqueId,
            Nickname = client.Nickname,
            ChannelId = client.ChannelId,
            ChannelName = channelNames.GetValueOrDefault(client.ChannelId, client.ChannelName),
            Platform = client.Platform,
            Version = client.Version,
            IdleTimeSeconds = client.IdleTimeSeconds,
            ConnectionTimeSeconds = client.ConnectionTimeSeconds,
            IsServerQuery = client.IsServerQuery
        }).ToList();

        var bans = teamspeak.Bans.Select(ban => new TeamspeakBan
        {
            ServerId = serverId,
            BanId = ban.BanId,
            Ip = ban.Ip,
            UniqueId = ban.UniqueId,
            Nickname = ban.Nickname,
            Reason = ban.Reason,
            Duration = ban.Duration,
            Created = ban.Created
        }).ToList();

        await heartbeatRepo.ReplaceTeamspeakDataAsync(serverId, state, channels, clients, bans, ct).ConfigureAwait(false);
    }

    private async Task UpdatePortsentryDataAsync(int serverId, PortsentryDataDto portsentry, CancellationToken ct)
    {
        PortsentryState? state = portsentry.IsInstalled ? new PortsentryState
        {
            ServerId = serverId,
            IsRunning = portsentry.IsRunning,
            Version = portsentry.Version,
            Mode = portsentry.Mode,
            TcpPorts = portsentry.TcpPorts,
            UdpPorts = portsentry.UdpPorts,
            BlockedCount = portsentry.BlockedCount
        } : null;

        var blockedIps = portsentry.BlockedIps.Select(b => new PortsentryBlockedIp
        {
            ServerId = serverId,
            IpAddress = b.IpAddress,
            Protocol = b.Protocol,
            BlockedAt = b.BlockedAt,
            Reason = b.Reason
        }).ToList();

        await heartbeatRepo.ReplacePortsentryDataAsync(serverId, state, blockedIps, ct).ConfigureAwait(false);
    }

    private async Task UpdateRkhunterDataAsync(int serverId, RkhunterDataDto rkhunter, CancellationToken ct)
    {
        RkhunterState? state = rkhunter.IsInstalled ? new RkhunterState
        {
            ServerId = serverId,
            Version = rkhunter.Version,
            DatabaseVersion = rkhunter.DatabaseVersion,
            LastScanTime = rkhunter.LastScanTime,
            LastScanStatus = rkhunter.LastScanStatus,
            WarningCount = rkhunter.WarningCount,
            DatabaseLastUpdated = rkhunter.DatabaseLastUpdated
        } : null;

        await heartbeatRepo.ReplaceRkhunterDataAsync(serverId, state, [], ct).ConfigureAwait(false);
    }

    private async Task UpdateSecurityUpdatesDataAsync(int serverId, SecurityUpdatesDataDto updates, CancellationToken ct)
    {
        // No-fake: persist only when the box runs apt. A successful probe stores trustworthy counts + the
        // (capped) package list; a failed probe stores Probed=false (UNKNOWN), never a fabricated "0 = up to date".
        SecurityUpdatesState? state = updates.PackageManagerPresent ? new SecurityUpdatesState
        {
            ServerId = serverId,
            Probed = updates.ProbeSucceeded,
            PendingTotal = updates.PendingTotal,
            PendingSecurity = updates.PendingSecurity,
            UpdatesJson = updates.ProbeSucceeded && updates.Updates.Count > 0
                ? JsonSerializer.Serialize(updates.Updates.Take(100).ToList())
                : null,
            CheckedAt = timeProvider.GetUtcNow().UtcDateTime
        } : null;

        await heartbeatRepo.ReplaceSecurityUpdatesDataAsync(serverId, state, ct).ConfigureAwait(false);
    }

    private async Task UpdateFirewallDataAsync(int serverId, FirewallDataDto firewall, CancellationToken ct)
    {
        // Persist only when ufw is present. Rules JSON is stored only when the helper read succeeded
        // (rules non-empty or active known); an unreadable firewall keeps Installed with no rules.
        FirewallState? state = firewall.Installed ? new FirewallState
        {
            ServerId = serverId,
            Installed = true,
            Active = firewall.Active,
            StatusKnown = firewall.StatusKnown,
            CollectionDiagnostics = firewall.CollectionDiagnostics,
            RulesJson = firewall.Rules.Count > 0 ? JsonSerializer.Serialize(firewall.Rules) : null
        } : null;

        await heartbeatRepo.ReplaceFirewallDataAsync(serverId, state, ct).ConfigureAwait(false);
    }
}
