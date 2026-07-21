// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.Auth;

public class AuthRepository(AppDbContext db, TimeProvider timeProvider) : IAuthRepository
{
    public async Task<RegistrationToken?> FindValidRegistrationTokenAsync(string token, CancellationToken ct = default)
    {
        return await db.RegistrationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Token == token
                                      && !t.IsUsed
                                      && t.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
    }

    public async Task<Server?> FindServerByHostnameAsync(string hostname, CancellationToken ct = default)
    {
        return await db.Servers
            .FirstOrDefaultAsync(s => s.Hostname == hostname, ct).ConfigureAwait(false);
    }

    public async Task<User?> FindUserWithRolesAsync(string username, CancellationToken ct = default)
    {
        return await db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct)
            .ConfigureAwait(false);
    }

    public async Task<User?> FindUserByIdWithRolesAsync(int userId, CancellationToken ct = default)
    {
        return await db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> AnyUsersExistAsync(CancellationToken ct = default)
    {
        return await db.Users.AnyAsync(ct).ConfigureAwait(false);
    }

    public async Task AddUserAsync(User user, CancellationToken ct = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public void AddServer(Server server)
    {
        db.Servers.Add(server);
    }

    public void AddServerToken(ServerToken serverToken)
    {
        db.ServerTokens.Add(serverToken);
    }

    public void AddRegistrationToken(RegistrationToken token)
    {
        db.RegistrationTokens.Add(token);
    }

    public async Task<List<RegistrationToken>> GetRegistrationTokensAsync(CancellationToken ct = default)
    {
        return await db.RegistrationTokens
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<RegistrationToken?> FindRegistrationTokenByIdAsync(int id, CancellationToken ct = default)
    {
        return await db.RegistrationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
    }

    public async Task<int?> ValidateServerTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var serverToken = await db.ServerTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash
                                      && !t.IsRevoked
                                      && t.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);
        return serverToken?.ServerId;
    }

    public async Task RevokeServerTokensAsync(int serverId, CancellationToken ct = default)
    {
        var activeTokens = await db.ServerTokens
            .Where(t => t.ServerId == serverId && !t.IsRevoked)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var token in activeTokens)
            token.IsRevoked = true;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public Task<DateTime?> GetLatestActiveServerTokenExpiryAsync(int serverId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return db.ServerTokens
            .AsNoTracking()
            .Where(t => t.ServerId == serverId && !t.IsRevoked && t.ExpiresAt > now)
            .OrderByDescending(t => t.ExpiresAt)
            .Select(t => (DateTime?)t.ExpiresAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> DeleteExpiredServerTokensAsync(int serverId, CancellationToken ct = default)
    {
        // Load + RemoveRange (not ExecuteDelete): the dead set per server is tiny
        // and this stays provider-agnostic (EF InMemory, used by integration
        // tests, does not support ExecuteDelete). Flushed by the caller's
        // SaveChangesAsync together with the freshly-minted token (atomic).
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var dead = await db.ServerTokens
            .Where(t => t.ServerId == serverId && (t.IsRevoked || t.ExpiresAt <= now))
            .ToListAsync(ct).ConfigureAwait(false);
        if (dead.Count == 0) return 0;
        db.ServerTokens.RemoveRange(dead);
        return dead.Count;
    }

    public async Task<bool> ServerExistsAsync(int serverId, CancellationToken ct = default)
    {
        return await db.Servers.AnyAsync(s => s.Id == serverId, ct).ConfigureAwait(false);
    }

    public async Task<bool> ServerHasActiveTokensAsync(int serverId, CancellationToken ct = default)
    {
        return await db.ServerTokens
            .AnyAsync(t => t.ServerId == serverId && !t.IsRevoked && t.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime, ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> ConsumeRegistrationTokenAsync(int tokenId, int? serverId, CancellationToken ct = default)
    {
        var token = await db.RegistrationTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId && !t.IsUsed, ct)
            .ConfigureAwait(false);

        if (token is null)
            return false;

        token.IsUsed = true;
        token.UsedByServerId = serverId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task LinkServerToEnvironmentsByNameAsync(
        int serverId, IReadOnlyCollection<string> environmentNames, CancellationToken ct = default)
    {
        var environmentIds = await db.Environments
            .Where(environment => environmentNames.Contains(environment.Name))
            .Select(environment => environment.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        var existingIds = await db.EnvironmentServers
            .Where(link => link.ServerId == serverId && environmentIds.Contains(link.EnvironmentId))
            .Select(link => link.EnvironmentId)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var environmentId in environmentIds.Except(existingIds))
            db.EnvironmentServers.Add(new EnvironmentServer { EnvironmentId = environmentId, ServerId = serverId });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<ExternalLogin?> FindExternalLoginAsync(string provider, string providerSubjectId, CancellationToken ct = default)
    {
        return await db.ExternalLogins
            .AsNoTracking()
            .Include(e => e.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(e => e.Provider == provider && e.ProviderSubjectId == providerSubjectId, ct)
            .ConfigureAwait(false);
    }

    public async Task AddExternalLoginAsync(ExternalLogin externalLogin, CancellationToken ct = default)
    {
        db.ExternalLogins.Add(externalLogin);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<string?> GetSecurityStampAsync(int userId, CancellationToken ct = default)
    {
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SecurityStamp)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task BumpSecurityStampAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null) return;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // --- Lockout (F-011) ---

    public async Task IncrementFailedLoginAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null) return;
        user.FailedLoginCount++;
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ResetFailedLoginAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null) return;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task LockoutUserAsync(int userId, DateTime lockoutEndUtc, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null) return;
        user.LockoutEndUtc = lockoutEndUtc;
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearLockoutAsync(int userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null) return;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // --- Refresh Tokens (F-012) ---

    public async Task AddRefreshTokenAsync(RefreshToken token, CancellationToken ct = default)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<RefreshToken?> FindRefreshTokenByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        return await db.RefreshTokens
            .Include(r => r.User).ThenInclude(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct)
            .ConfigureAwait(false);
    }

    public async Task RevokeRefreshTokenAsync(int tokenId, int? replacedById, CancellationToken ct = default)
    {
        var token = await db.RefreshTokens.FirstOrDefaultAsync(r => r.Id == tokenId, ct).ConfigureAwait(false);
        if (token is null) return;
        token.RevokedAt = timeProvider.GetUtcNow().UtcDateTime;
        token.ReplacedById = replacedById;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RevokeAllUserRefreshTokensAsync(int userId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var active = await db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > now)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var t in active)
            t.RevokedAt = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteExpiredRefreshTokensAsync(int userId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var dead = await db.RefreshTokens
            .Where(r => r.UserId == userId && (r.ExpiresAt <= now || (r.RevokedAt != null && r.RevokedAt < now.AddMinutes(-5))))
            .ToListAsync(ct).ConfigureAwait(false);
        if (dead.Count > 0)
            db.RefreshTokens.RemoveRange(dead);
        // Caller's SaveChangesAsync flushes
    }

    public async Task<int> DeleteAllExpiredRefreshTokensAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var grace = utcNow.AddMinutes(-5);
        var dead = await db.RefreshTokens
            .Where(r => r.ExpiresAt <= utcNow || (r.RevokedAt != null && r.RevokedAt < grace))
            .ToListAsync(ct).ConfigureAwait(false);
        if (dead.Count == 0)
            return 0;
        db.RefreshTokens.RemoveRange(dead);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return dead.Count;
    }

    // --- TOTP (F-010) ---

    public async Task<User?> FindUserByIdForUpdateAsync(int userId, CancellationToken ct = default)
    {
        return await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            .ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
