// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Aetheus.Shared.Helpers;
using Microsoft.AspNetCore.SignalR;

namespace Aetheus.Back.Components.Auth;

// Agent/server enrollment - extracted from AuthService into a focused collaborator (removes the
// former AuthService.ServerRegistration.cs partial split). Shares token primitives with AuthService
// via AuthTokenHelper.
public class ServerEnrollmentService(
    IAuthRepository repo,
    IConfiguration config,
    IAuditService audit,
    IHubContext<ServerHub> serverHub,
    IAdminChangeNotifier adminNotifier,
    JwtOptions jwtOptions,
    TimeProvider timeProvider) : IServerEnrollmentService
{
    public async Task<ServerRegistrationResponse?> RegisterServerAsync(ServerRegistrationRequest request, CancellationToken ct = default)
    {
        // Hardening (High #6): repo lookup now compares the SHA-256 hash, not plaintext, so
        // a DB read does not yield usable registration tokens.
        var regToken = await repo.FindValidRegistrationTokenAsync(AuthTokenHelper.HashToken(jwtOptions.SigningKey, request.RegistrationToken), ct).ConfigureAwait(false);
        if (regToken is null) return null;

        var server = await repo.FindServerByHostnameAsync(request.Hostname, ct).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (server is not null)
        {
            if (await repo.ServerHasActiveTokensAsync(server.Id, ct).ConfigureAwait(false))
                await repo.RevokeServerTokensAsync(server.Id, ct).ConfigureAwait(false);

            server.OsDescription = request.OsDescription;
            server.OsType = OsTypeHelper.FromDescription(request.OsDescription);
            server.IpAddress = request.IpAddress;
            server.AgentVersion = request.AgentVersion;
            server.AgentInstalledAt = request.AgentInstalledAt;
            server.DockerAvailable = request.DockerAvailable;
            server.InsecureTls = request.InsecureTls;
            server.Status = ServerStatus.Online;
            server.LastHeartbeat = now;
            server.UpdatedAt = now;
            // Re-derive the runner gate from the reported toolchain (a re-install is an explicit
            // operator action). A pre-feature agent omits it (null) → preserve the existing choice.
            if (request.PipelineRunnerAvailable is { } reReg)
                server.PipelineRunnerEnabled = reReg;
        }
        else
        {
            server = new Server
            {
                Name = request.Hostname,
                Hostname = request.Hostname,
                OsDescription = request.OsDescription,
                OsType = OsTypeHelper.FromDescription(request.OsDescription),
                IpAddress = request.IpAddress,
                AgentVersion = request.AgentVersion,
                AgentInstalledAt = request.AgentInstalledAt,
                DockerAvailable = request.DockerAvailable,
                InsecureTls = request.InsecureTls,
                Status = ServerStatus.Online,
                LastHeartbeat = now,
                OrganizationId = regToken.OrganizationId,
                // Default the runner gate from the reported toolchain instead of authorising every box
                // (secure-by-default, per the migration/DTO contract). A pre-feature agent omits it
                // (null) → keep the legacy enabled default.
                PipelineRunnerEnabled = request.PipelineRunnerAvailable ?? true
            };
            repo.AddServer(server);
        }
        var bearerToken = AuthTokenHelper.GenerateSecureToken();
        var tokenHash = AuthTokenHelper.HashToken(jwtOptions.SigningKey, bearerToken);
        var ttlDays = config.GetValue("AgentToken:TtlDays", 365);
        repo.AddServerToken(new ServerToken
        {
            Server = server,
            TokenHash = tokenHash,
            ExpiresAt = timeProvider.GetUtcNow().UtcDateTime.AddDays(ttlDays)
        });

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        if (!await repo.ConsumeRegistrationTokenAsync(regToken.Id, server.Id, ct).ConfigureAwait(false))
            return null;

        await audit.LogAsync("Registered", "Server", server.Id, server.Hostname, ct).ConfigureAwait(false);
        // S-FEAT-RT2W: the token just flipped to used - refresh any open admin token list in realtime.
        await adminNotifier.BroadcastAsync(AdminEntities.RegistrationToken, regToken.Id, EntityChangeOps.Updated, ct).ConfigureAwait(false);

        // RBAC scoping (audit 360): every target group is access-filtered by the F-05 fan-out in
        // ServerHub.JoinAllServers - admins (full access) sit in AllServers, server-{id} only contains
        // connections that passed a Read check on this server, and server-org-{org} only contains members
        // of this server's organization (org membership grants Read on every org server). The org group
        // is what reaches a non-admin member in realtime, since server-{id} is empty for a brand-new
        // server. So this broadcast never reaches an unauthorized subscriber. The payload omits IpAddress.
        await serverHub.Clients.Groups([HubGroups.AllServers, HubGroups.Server(server.Id), HubGroups.ServerOrg(server.OrganizationId)]).SendAsync("ServerRegistered", new ServerDto
        {
            Id = server.Id,
            Name = server.Name,
            Hostname = server.Hostname,
            OsDescription = server.OsDescription,
            AgentVersion = server.AgentVersion,
            Status = server.Status,
            Type = server.Type,
            LastHeartbeat = server.LastHeartbeat,
            Tags = TagsHelper.DeserializeTags(server.Tags),
            CreatedAt = server.CreatedAt,
            OrganizationId = server.OrganizationId,
            AgentInstalledAt = server.AgentInstalledAt
        }, ct).ConfigureAwait(false);

        return new ServerRegistrationResponse
        {
            ServerId = server.Id,
            BearerToken = bearerToken
        };
    }
}
