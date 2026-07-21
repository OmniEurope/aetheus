// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.Settings;

public interface ISettingsService
{
    Task<List<AppSettingDto>> GetSettingsAsync(CancellationToken ct = default);
    Task UpdateSettingAsync(string key, string value, CancellationToken ct = default);
    Task<List<SecretDto>> GetSecretsAsync(CancellationToken ct = default);
    Task<SecretDto> CreateSecretAsync(CreateSecretRequest request, CancellationToken ct = default);
    Task<bool> DeleteSecretAsync(int id, CancellationToken ct = default);
    Task<string?> GetSettingValueAsync(string key, CancellationToken ct = default);
}
