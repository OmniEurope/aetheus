// SPDX-License-Identifier: EUPL-1.2

using Aetheus.Back.Services;

namespace Aetheus.Back.Components.Mail;

/// <summary>
/// PLAN-005 lot 5: re-verifies the DNS of active mail domains every six hours (at most 50 domains per pass,
/// oldest check first) so the SPF / DKIM / DMARC badges follow DNS changes made outside Aetheus. A pass is
/// idempotent, so a second replica running it concurrently only repeats lookups.
/// </summary>
public sealed class MailDnsRefreshService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<MailDnsRefreshService> logger,
    IPostgresLeaderLease? leaderLease = null) : PeriodicBackgroundService(TimeSpan.FromHours(6), leaderLease, "aetheus:mail-dns-refresh")
{
    internal const int DomainsPerPass = 50;
    internal static readonly TimeSpan RecheckAfter = TimeSpan.FromHours(6);

    protected override Task ExecuteIterationAsync(CancellationToken ct) => RefreshAsync(ct);

    protected override void LogIterationError(Exception exception) =>
        logger.LogError(exception, "Error during the mail DNS refresh");

    internal async Task<int> RefreshAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IMailInventoryRepository>();
        var verifier = scope.ServiceProvider.GetRequiredService<IMailDnsVerifier>();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var domains = await repo.GetTrackedDomainsDueForDnsCheckAsync(now - RecheckAfter, DomainsPerPass, ct).ConfigureAwait(false);
        if (domains.Count == 0) return 0;

        var hostnames = new Dictionary<int, string?>();
        foreach (var domain in domains)
        {
            if (!hostnames.TryGetValue(domain.ServerId, out var hostname))
            {
                hostname = (await repo.GetStateAsync(domain.ServerId, ct).ConfigureAwait(false))?.Hostname;
                hostnames[domain.ServerId] = hostname;
            }
            await verifier.VerifyAsync(domain, hostname, ct).ConfigureAwait(false);
        }
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        logger.LogInformation("Mail DNS refresh verified {Count} domains", domains.Count);
        return domains.Count;
    }
}
