// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Settings;

public class SettingsRepository(AppDbContext db) : ISettingsRepository
{
    public async Task<List<AppSetting>> GetAllSettingsAsync(CancellationToken ct = default)
    {
        return await db.AppSettings
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<AppSetting?> FindSettingByKeyAsync(string key, CancellationToken ct = default)
    {
        return await db.AppSettings
            .FindAsync([key], ct)
            .ConfigureAwait(false);
    }

    public async Task AddSettingAsync(AppSetting setting, CancellationToken ct = default)
    {
        db.AppSettings.Add(setting);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<Secret>> GetAllSecretsAsync(CancellationToken ct = default)
    {
        return await db.Secrets
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<Secret?> FindSecretByKeyAsync(string key, CancellationToken ct = default)
    {
        return await db.Secrets
            .FirstOrDefaultAsync(s => s.Key == key, ct)
            .ConfigureAwait(false);
    }

    public async Task<Secret?> FindSecretByIdAsync(int id, CancellationToken ct = default)
    {
        return await db.Secrets
            .FindAsync([id], ct)
            .ConfigureAwait(false);
    }

    public async Task AddSecretAsync(Secret secret, CancellationToken ct = default)
    {
        db.Secrets.Add(secret);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveSecretAsync(Secret secret, CancellationToken ct = default)
    {
        db.Secrets.Remove(secret);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
