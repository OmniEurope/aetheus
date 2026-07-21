// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Exceptions;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.ModuleLinks;

public class ModuleLinkService(IModuleLinkRepository repo) : IModuleLinkService
{
    public async Task<List<ModuleLinkDto>> GetLinksAsync(int serverId, CancellationToken ct = default)
    {
        var links = await repo.GetLinksAsync(serverId, ct).ConfigureAwait(false);
        return links.Select(MapToDto).ToList();
    }

    public async Task<List<LinkedResourceDto>> GetLinksForResourceAsync(int serverId, ModuleLinkType sourceType, string sourceIdentifier, CancellationToken ct = default)
    {
        var links = await repo.GetLinksForResourceAsync(serverId, sourceType, sourceIdentifier, ct).ConfigureAwait(false);
        return links.Select(l =>
        {
            // Return the "other" side of the link
            var isSource = l.SourceType == sourceType && l.SourceIdentifier == sourceIdentifier;
            return new LinkedResourceDto
            {
                LinkId = l.Id,
                Type = isSource ? l.TargetType : l.SourceType,
                Identifier = isSource ? l.TargetIdentifier : l.SourceIdentifier,
                IsAutoDetected = l.IsAutoDetected
            };
        }).ToList();
    }

    public async Task<PaginatedResult<LinkedResourceDto>> GetLinksPageAsync(
        int serverId, ModuleLinkPageRequest request, CancellationToken ct = default)
    {
        var (page, pageSize) = request.Normalize();
        if (request.ResourceIdentifiers.Count == 0)
        {
            return new PaginatedResult<LinkedResourceDto>
            {
                Page = page,
                PageSize = pageSize
            };
        }

        var (links, totalCount) = await repo.GetLinksPageAsync(serverId, request, ct).ConfigureAwait(false);
        var identifiers = request.ResourceIdentifiers.ToHashSet(StringComparer.Ordinal);
        return new PaginatedResult<LinkedResourceDto>
        {
            Items = links.Select(link => MapLinkedResource(link, request.SourceType, identifiers)).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ModuleLinkDto> CreateLinkAsync(int serverId, CreateModuleLinkRequest request, CancellationToken ct = default)
    {
        if (await repo.LinkExistsAsync(serverId, request.SourceType, request.SourceIdentifier, request.TargetType, request.TargetIdentifier, ct).ConfigureAwait(false))
            throw new ConflictException("Link already exists.");

        var link = new ModuleLink
        {
            ServerId = serverId,
            SourceType = request.SourceType,
            SourceIdentifier = request.SourceIdentifier,
            TargetType = request.TargetType,
            TargetIdentifier = request.TargetIdentifier,
            IsAutoDetected = false
        };

        await repo.AddAsync(link, ct).ConfigureAwait(false);
        return MapToDto(link);
    }

    public async Task DeleteLinkAsync(int linkId, CancellationToken ct = default)
    {
        var link = await repo.FindLinkAsync(linkId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("Link not found.");
        await repo.RemoveAsync(link, ct).ConfigureAwait(false);
    }

    public async Task<int?> GetServerIdForLinkAsync(int linkId, CancellationToken ct = default)
    {
        var link = await repo.FindLinkAsync(linkId, ct).ConfigureAwait(false);
        return link?.ServerId;
    }

    public async Task AutoDetectLinksAsync(int serverId, CancellationToken ct = default)
    {
        // Remove old auto-detected links
        await repo.RemoveAutoDetectedAsync(serverId, ct).ConfigureAwait(false);

        // Load Apache vhosts and Certbot certs for this server
        var vhosts = await repo.GetApacheVhostsAsync(serverId, ct).ConfigureAwait(false);
        var certs = await repo.GetCertbotCertsAsync(serverId, ct).ConfigureAwait(false);

        var newLinks = new List<ModuleLink>();

        // Auto-detect: Apache VHost ServerName ↔ Certbot cert domains
        foreach (var vhost in vhosts)
        {
            foreach (var cert in certs)
            {
                var domains = DeserializeDomains(cert.Domains);
                if (domains.Contains(vhost.ServerName, StringComparer.OrdinalIgnoreCase))
                {
                    newLinks.Add(new ModuleLink
                    {
                        ServerId = serverId,
                        SourceType = ModuleLinkType.Apache,
                        SourceIdentifier = vhost.ServerName,
                        TargetType = ModuleLinkType.Certbot,
                        TargetIdentifier = cert.Name,
                        IsAutoDetected = true
                    });
                }
            }
        }

        // Deduplicate and save
        var seen = new HashSet<string>();
        var deduped = new List<ModuleLink>();
        foreach (var link in newLinks)
        {
            var key = $"{link.SourceType}:{link.SourceIdentifier}:{link.TargetType}:{link.TargetIdentifier}";
            if (seen.Add(key))
                deduped.Add(link);
        }

        if (deduped.Count > 0)
            await repo.AddLinksAsync(deduped, ct).ConfigureAwait(false);
    }

    private static ModuleLinkDto MapToDto(ModuleLink link) => new()
    {
        Id = link.Id,
        SourceType = link.SourceType,
        SourceIdentifier = link.SourceIdentifier,
        TargetType = link.TargetType,
        TargetIdentifier = link.TargetIdentifier,
        IsAutoDetected = link.IsAutoDetected
    };

    private static LinkedResourceDto MapLinkedResource(
        ModuleLink link, ModuleLinkType sourceType, HashSet<string> identifiers)
    {
        var sourceMatches = link.SourceType == sourceType && identifiers.Contains(link.SourceIdentifier);
        return new LinkedResourceDto
        {
            LinkId = link.Id,
            Type = sourceMatches ? link.TargetType : link.SourceType,
            Identifier = sourceMatches ? link.TargetIdentifier : link.SourceIdentifier,
            IsAutoDetected = link.IsAutoDetected
        };
    }

    private static List<string> DeserializeDomains(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
