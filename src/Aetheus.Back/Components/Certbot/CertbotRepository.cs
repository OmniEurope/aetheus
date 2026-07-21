// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Certbot;

public class CertbotRepository(AppDbContext db) : ICertbotRepository
{
    public async Task<List<CertbotCertificate>> GetCertificatesAsync(int serverId, CancellationToken ct = default)
    {
        return await db.CertbotCertificates
            .AsNoTracking()
            .Where(c => c.ServerId == serverId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Servers
            .AnyAsync(s => s.Id == serverId, ct)
            .ConfigureAwait(false);
    }

    public async Task AddTaskAsync(ServerTask task, CancellationToken ct = default)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
