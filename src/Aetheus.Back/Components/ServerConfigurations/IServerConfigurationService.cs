// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.ServerConfigurations;

public interface IServerConfigurationService
{
    Task<string?> ExportConfigurationAsync(int serverId, CancellationToken ct = default);
    Task<ServerConfigValidationResult> ValidateConfigurationAsync(string yaml, CancellationToken ct = default);
    Task<ServerConfigPreviewDto?> PreviewImportAsync(int serverId, string yaml, CancellationToken ct = default);
    Task<ServerConfigDeployResultDto?> DeployConfigurationAsync(int serverId, string yaml, CancellationToken ct = default);
}
