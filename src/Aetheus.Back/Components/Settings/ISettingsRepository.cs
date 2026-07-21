// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Settings;

public interface ISettingsRepository
{
    Task<List<AppSetting>> GetAllSettingsAsync(CancellationToken ct = default);
    Task<AppSetting?> FindSettingByKeyAsync(string key, CancellationToken ct = default);
    Task AddSettingAsync(AppSetting setting, CancellationToken ct = default);
    Task<List<Secret>> GetAllSecretsAsync(CancellationToken ct = default);
    Task<Secret?> FindSecretByKeyAsync(string key, CancellationToken ct = default);
    Task<Secret?> FindSecretByIdAsync(int id, CancellationToken ct = default);
    Task AddSecretAsync(Secret secret, CancellationToken ct = default);
    Task RemoveSecretAsync(Secret secret, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
