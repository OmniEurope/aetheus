// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

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

    public Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default) =>
        ServerTaskRepositoryOperations.ServerExistsAsync(db, serverId, ct);

    public Task AddTaskAsync(ServerTask task, CancellationToken ct = default) =>
        ServerTaskRepositoryOperations.AddTaskAsync(db, task, ct);
}
