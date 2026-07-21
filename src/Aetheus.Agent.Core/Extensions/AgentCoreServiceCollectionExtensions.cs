// SPDX-License-Identifier: EUPL-1.2
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aetheus.Agent.Core.Collectors;
using Aetheus.Agent.Core.Configuration;
using Aetheus.Agent.Core.Executors;
using Aetheus.Agent.Core.Operations;
using Aetheus.Agent.Core.Plugins;
using Aetheus.Agent.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aetheus.Agent.Core.Extensions;

public static class AgentCoreServiceCollectionExtensions
{
    public static IServiceCollection AddAgentCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AetheusAgentOptions>()
            .Bind(configuration.GetSection(AetheusAgentOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AetheusAgentOptions>, AetheusAgentOptionsValidator>();

        services.PostConfigure<AetheusAgentOptions>(opts =>
        {
            if (string.IsNullOrEmpty(opts.WorkDirectory))
            {
                opts.WorkDirectory = OperatingSystem.IsWindows()
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Aetheus", "Agent")
                    : "/var/lib/aetheus-agent";
            }

            if (string.IsNullOrEmpty(opts.PluginDirectory))
                opts.PluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");

            var storage = opts.DockerStorageMaintenance;
            storage.MaintenanceIntervalMinutes = Math.Clamp(storage.MaintenanceIntervalMinutes, 5, 1440);
            storage.MaxCacheAgeHours = Math.Clamp(storage.MaxCacheAgeHours, 1, 24 * 365);
            storage.PressureCacheAgeHours = Math.Clamp(storage.PressureCacheAgeHours, 1, storage.MaxCacheAgeHours);
            storage.ReservedSpaceGiB = Math.Clamp(storage.ReservedSpaceGiB, 1, 10_000);
            storage.MaxCacheGiB = Math.Clamp(storage.MaxCacheGiB, storage.ReservedSpaceGiB, 10_000);
            storage.MinFreeSpaceGiB = Math.Clamp(storage.MinFreeSpaceGiB, 1, 10_000);
            storage.PressureUsedPercent = Math.Clamp(storage.PressureUsedPercent, 1, 99);
            storage.NuGetCacheRetentionDays = Math.Clamp(storage.NuGetCacheRetentionDays, 0, 3650);

            var dangerousPatterns = new[] { ".*", "^.*$", ".+", "^.+$", "^.{0,}$" };
            opts.AllowedCommandPatterns.RemoveAll(p =>
                dangerousPatterns.Contains(p, StringComparer.Ordinal));
        });

        // Resolve options for HTTP client setup
        var options = configuration
            .GetSection(AetheusAgentOptions.SectionName)
            .Get<AetheusAgentOptions>()!;

        ValidateServerUrl(options.ServerUrl);
        ValidateTlsPosture(options);

        // Core services
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AgentState>();
        services.AddSingleton<AgentRuntimeHealth>();
        services.AddSingleton<IAgentProcessRestarter, AgentProcessRestarter>();
        services.AddSingleton<IShellRunner, DefaultShellRunner>();
        services.AddSingleton<DockerStorageMaintenanceService>();
        services.AddSingleton<IDockerStorageMaintenance>(sp => sp.GetRequiredService<DockerStorageMaintenanceService>());
        services.AddTransient<BearerTokenHandler>();
        // Control-plane RPCs (heartbeat, claim, start/complete, log batches): small JSON
        // payloads where a network blip must NOT fail a real build. The standard resilience
        // pipeline owns the timeouts (10s per attempt, 30s total, 3 retries with backoff),
        // so the client timeout is disabled to avoid racing it.
        services.AddHttpClient("AetheusServer", client =>
        {
            client.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .ConfigurePrimaryHttpMessageHandler(() => BuildPrimaryHandler(options))
        .AddHttpMessageHandler<BearerTokenHandler>()
        .AddStandardResilienceHandler();

        // Streamed transfers (artifact upload/download, agent archive): a per-attempt
        // timeout would abort any transfer larger than a few MB, and replaying a partially
        // consumed stream is not safe - no resilience here, just a generous timeout.
        services.AddHttpClient("AetheusServerTransfer", client =>
        {
            client.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/");
            client.Timeout = AgentRuntimeDefaults.BackendRequestTimeout;
        })
        .ConfigurePrimaryHttpMessageHandler(() => BuildPrimaryHandler(options))
        .AddHttpMessageHandler<BearerTokenHandler>();

        // App-availability probe client (PLAN-001): plain outbound calls to localhost/LAN apps, zero
        // elevation, no auth token, no redirects. TLS validation is relaxed because a probed app may use
        // a self-signed cert and this channel authenticates nothing - it only checks reachability/status.
        services.AddHttpClient(AppProbeWorker.ProbeHttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan; // per-probe timeout is enforced via a linked CTS
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }
        });

        services.AddSingleton<IServerApiClient, ServerApiClient>();
        services.AddSingleton<EnrollmentService>();
        services.AddSingleton<IEnrollmentService>(sp => sp.GetRequiredService<EnrollmentService>());
        services.AddSingleton<ICommandValidator, CommandValidator>();
        services.AddSingleton<DockerCollector>();
        services.AddSingleton<IDockerCollector>(sp => sp.GetRequiredService<DockerCollector>());
        services.AddSingleton<ApacheCollector>();
        services.AddSingleton<IApacheCollector>(sp => sp.GetRequiredService<ApacheCollector>());
        services.AddSingleton<CertbotCollector>();
        services.AddSingleton<ICertbotCollector>(sp => sp.GetRequiredService<CertbotCollector>());
        services.AddSingleton<MailCollector>();
        services.AddSingleton<IMailCollector>(sp => sp.GetRequiredService<MailCollector>());
        services.AddSingleton<ITeamspeakQueryClient, TcpTeamspeakQueryClient>();
        services.AddSingleton<TeamspeakCollector>();
        services.AddSingleton<ITeamspeakCollector>(sp => sp.GetRequiredService<TeamspeakCollector>());
        services.AddSingleton<PortsentryCollector>();
        services.AddSingleton<IPortsentryCollector>(sp => sp.GetRequiredService<PortsentryCollector>());
        services.AddSingleton<RkhunterCollector>();
        services.AddSingleton<IRkhunterCollector>(sp => sp.GetRequiredService<RkhunterCollector>());
        services.AddSingleton<SecurityUpdatesCollector>();
        services.AddSingleton<ISecurityUpdatesCollector>(sp => sp.GetRequiredService<SecurityUpdatesCollector>());
        services.AddSingleton<FirewallCollector>();
        services.AddSingleton<IFirewallCollector>(sp => sp.GetRequiredService<FirewallCollector>());
        services.AddSingleton<ISudoersHashCollector, SudoersHashCollector>();

        // Docker executor (cross-platform)
        services.AddSingleton<IExecutor, DockerExecutor>();

        // Phase 2: ephemeral hardened container executor for isolation: container pipeline steps.
        services.AddSingleton<IContainerExecutor, ContainerRunExecutor>();

        // F-32: typed operation executors. These run platform commands via fixed argument lists
        // - no shell, no allow-list regex, target string validated per kind.
        services.AddSingleton<IOperationExecutor, DockerOperationExecutor>();
        services.AddSingleton<IOperationExecutor, ServiceOperationExecutor>();
        services.AddSingleton<IOperationExecutor, PackageOperationExecutor>();
        services.AddSingleton<IOperationExecutor, SystemPackageUpgradeExecutor>();
        services.AddSingleton<IOperationExecutor, FirewallOperationExecutor>();
        services.AddSingleton<IOperationExecutor, BackupOperationExecutor>();
        services.AddSingleton<IOperationExecutor, ApacheOperationExecutor>();
        services.AddSingleton<IOperationExecutor, CertbotOperationExecutor>();
        services.AddSingleton<IOperationExecutor, RkhunterOperationExecutor>();
        services.AddSingleton<IOperationExecutor, AgentSelfUpdateOperationExecutor>();
        services.AddSingleton<IOperationExecutor, MailOperationExecutor>();
        services.AddSingleton<IOperationExecutor, PortsentryOperationExecutor>();
        services.AddSingleton<IOperationExecutor, CronOperationExecutor>();
        services.AddSingleton<IOperationExecutor, PipelineArtifactOperationExecutor>();
        services.AddSingleton<IOperationExecutor, TeamspeakServerQueryOperationExecutor>();
        services.AddSingleton<IOperationExecutor, TeamspeakSetupOperationExecutor>();
        services.AddSingleton<IOperationExecutor, TeamspeakGracefulRestartOperationExecutor>();
        services.AddSingleton<IOperationExecutor, DeployOperationExecutor>();

        // Plugins
        PluginLoader.AddPluginLoader(services);

        // Background services
        services.AddHostedService<AgentStartupService>();
        services.AddHostedService<HeartbeatService>();
        services.AddHostedService<PollingService>();
        services.AddHostedService<AgentLivenessWatchdogService>();
        services.AddHostedService<AppProbeWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<DockerStorageMaintenanceService>());

        return services;
    }

    private static SocketsHttpHandler BuildPrimaryHandler(AetheusAgentOptions options) => new()
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            RemoteCertificateValidationCallback = BuildCertValidationCallback(options)
        }
    };

    // S-FEAT-24: choose the TLS validation strategy. Pinning (preferred for the dev cert) accepts the
    // server cert only when its SHA-256 thumbprint matches the configured pin - far narrower than the
    // accept-any AllowInsecureCerts escape hatch. Falls back to accept-any, then to default validation.
    internal static RemoteCertificateValidationCallback? BuildCertValidationCallback(AetheusAgentOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PinnedServerCertThumbprint))
        {
            var pinned = options.PinnedServerCertThumbprint.Replace(":", "", StringComparison.Ordinal).Trim();
            return (_, cert, _, _) =>
            {
                if (cert is null) return false;
                using var cert2 = new X509Certificate2(cert);
                var thumb = cert2.GetCertHashString(HashAlgorithmName.SHA256);
                return string.Equals(thumb, pinned, StringComparison.OrdinalIgnoreCase);
            };
        }

        return options.AllowInsecureCerts ? (_, _, _, _) => true : null;
    }

    // AllowInsecureCerts disables ALL TLS validation (agent channel, self-update download,
    // pipeline git clones). That is tolerable only against a local dev backend: loopback or
    // Docker Desktop's host alias (the VPS-sim enrolls via https://host.docker.internal).
    // Anywhere else, fail fast at startup instead of silently running a MITM-able agent;
    // self-signed remote backends must use PinnedServerCertThumbprint (which takes
    // precedence over AllowInsecureCerts) instead.
    internal static void ValidateTlsPosture(AetheusAgentOptions options)
    {
        if (!options.AllowInsecureCerts || !string.IsNullOrWhiteSpace(options.PinnedServerCertThumbprint))
            return;

        var host = new Uri(options.ServerUrl, UriKind.Absolute).Host;
        // Uri.Host returns the IPv6 literal WITH brackets ("[::1]"), so match both forms.
        var isLocalDev = host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "host.docker.internal";
        if (!isLocalDev)
        {
            throw new InvalidOperationException(
                $"AllowInsecureCerts=true is only allowed against a local dev backend (loopback or host.docker.internal); ServerUrl host is '{host}'. " +
                "For a self-signed remote backend, configure Aetheus:PinnedServerCertThumbprint instead.");
        }
    }

    private static void ValidateServerUrl(string serverUrl)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Invalid ServerUrl: {serverUrl}");

        var isLoopback = uri.Host is "localhost" or "127.0.0.1" or "::1";
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) && !isLoopback)
            throw new InvalidOperationException("ServerUrl must use HTTPS for non-loopback addresses.");
    }
}
