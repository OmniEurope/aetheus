// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Shared;

/// <summary>
/// The one read <see cref="ValidateServerExistsFilter"/> needs, over the shared <c>Servers</c> table.
/// An own read: the filter serves modules below Servers (Apache, Certbot, Docker...), so it cannot
/// reach up to the Servers module's repository (layer guard, 2026-09-25). Same query as
/// <c>ServerRepository.ServerExistsAsync</c>.
/// </summary>
public sealed class ServerExistenceRepository(AppDbContext db)
{
    public Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default) =>
        db.Servers.AnyAsync(server => server.Id == serverId, ct);
}
