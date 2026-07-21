// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Sockets;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data;
using Aetheus.Back.Services;

namespace Aetheus.Back.Components.Pipelines;

public static class PipelinesModuleExtensions
{
    public const string EnvironmentCheckHttpClient = "PipelineEnvironmentCheck";

    public static IServiceCollection AddPipelinesModule(this IServiceCollection services)
    {
        services.AddScoped<IPipelineRepository, PipelineRepository>();
        services.AddScoped<IPipelineGitService, PipelineGitService>();
        services.AddScoped<DemoContentSeeder>();
        services.AddScoped<IPipelineVariableResolver, PipelineVariableResolver>();
        services.AddScoped<IPipelineService, PipelineService>();
        services.AddScoped<IPipelineRunService, PipelineRunService>();
        services.AddScoped<IPipelineLauncher>(sp => (IPipelineLauncher)sp.GetRequiredService<IPipelineRunService>());
        services.AddScoped<IPipelineApprovalService, PipelineApprovalService>();
        services.AddScoped<IPipelineArtifactService, PipelineArtifactService>();
        services.AddScoped<IPipelineFleetRepository, PipelineFleetRepository>();
        services.AddScoped<IPipelineFleetService, PipelineFleetService>();
        services.AddScoped<IPipelineTemplateResolver, PipelineTemplateResolver>();
        services.AddScoped<IPipelineTemplateService, PipelineTemplateService>();
        services.AddScoped<IPipelineWebhookService, PipelineWebhookService>();
        services.AddSingleton<IPostgresLeaderLease, PostgresLeaderLease>();
        // Pipeline chaining: trigger downstream pipelines when an upstream run succeeds (on_success:).
        services.AddScoped<Services.DomainEvents.IDomainEventHandler<Events.PipelineRunCompletedEvent>, PipelineRunCompletedDownstreamHandler>();
        // Orchestration: complete a waiting `type: trigger` step when its child run finishes.
        services.AddScoped<Services.DomainEvents.IDomainEventHandler<Events.PipelineRunCompletedEvent>, PipelineRunCompletedTriggerHandler>();
        // SSRF hardening: the string-level IsSafeOutboundUrl guard in PipelineRunService can be
        // bypassed by DNS-rebinding (TOCTOU between the check and the request). Re-validate the
        // resolved IP at connect time via SocketsHttpHandler.ConnectCallback, mirroring the
        // "webhooks" client, so a short-TTL host that resolves to a forbidden address is rejected.
        services.AddHttpClient(EnvironmentCheckHttpClient)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectCallback = async (ctx, ct) =>
                {
                    var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct).ConfigureAwait(false);
                    var safe = addresses.FirstOrDefault(a => !WebhookSsrfGuard.IsForbiddenAddress(a))
                        ?? throw new HttpRequestException("Environment check target resolved to a forbidden address.");

                    var socket = new Socket(safe.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(safe, ctx.DnsEndPoint.Port, ct).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            });
        services.AddHostedService<PipelineSchedulerService>();
        services.AddHostedService<PipelineRetentionService>();
        services.AddHostedService<PipelineTriggerReconcileService>();
        return services;
    }
}
