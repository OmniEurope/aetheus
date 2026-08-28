// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Data;

internal static class ServerTaskRepositoryOperations
{
    public static Task<bool> ServerExistsAsync(
        AppDbContext db,
        int serverId,
        CancellationToken ct) =>
        db.Servers.AsNoTracking()
            .Select(server => server.Id)
            .ContainsAsync(serverId, ct);

    public static async Task AddTaskAsync(
        AppDbContext db,
        ServerTask task,
        CancellationToken ct)
    {
        db.Add(task);
        _ = await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
