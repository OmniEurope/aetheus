// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Pipelines;

public interface IPipelineTemplateService
{
    Task<List<PipelineTemplateSummaryDto>> GetTemplatesAsync(CancellationToken ct = default);
    Task<PipelineTemplateDto?> GetTemplateAsync(int id, CancellationToken ct = default);
    Task<PaginatedResult<PipelineTemplateVersionSummaryDto>> GetTemplateVersionsAsync(
        int id, PaginationRequest request, CancellationToken ct = default);
    Task<PipelineTemplateVersionDto?> GetTemplateVersionAsync(
        int id, int version, CancellationToken ct = default);
    Task<PipelineTemplateDto> CreateTemplateAsync(CreatePipelineTemplateRequest request, CancellationToken ct = default);
    Task<PipelineTemplateDto?> UpdateTemplateAsync(int id, UpdatePipelineTemplateRequest request, CancellationToken ct = default);
    Task<bool> DeleteTemplateAsync(int id, CancellationToken ct = default);
    Task<PipelineTemplateDto?> ImportTemplateAsync(
        string yamlContent, CancellationToken ct = default, int? organizationId = null);
    Task<string?> ResolveTemplateAsync(
        string yamlContent,
        Dictionary<string, string>? parameters = null,
        CancellationToken ct = default,
        int? organizationId = null);
}
