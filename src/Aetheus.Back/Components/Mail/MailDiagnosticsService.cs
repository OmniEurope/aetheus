// SPDX-License-Identifier: EUPL-1.2
using System.Globalization;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Mail;

public interface IMailDiagnosticsService
{
    Task<MailDiagnosticsDto> GetAsync(int serverId, CancellationToken ct = default);

    Task<MailMxPreviewDto> PreviewMxAsync(string domain, CancellationToken ct = default);

    /// <summary>Verifies the DNS of one domain now and persists its SPF / DKIM / DMARC flags.</summary>
    Task<MailDnsCheckDto> VerifyDomainDnsAsync(int serverId, int domainId, CancellationToken ct = default);
}

/// <summary>
/// PLAN-005 lot 7: a "why does my mail not work" checklist computed without SSH, from the last heartbeat
/// inventory and backend DNS lookups. Keys and details are stable codes the UI localises. The listening
/// ports are checked on the server itself by the <c>MailTestConfig</c> task (mail-manage check).
/// </summary>
public sealed class MailDiagnosticsService(
    IMailInventoryRepository inventory,
    IMailRepository mailRepo,
    IServerRepository serverRepo,
    IMailDnsResolver resolver,
    IMailDnsVerifier verifier,
    TimeProvider timeProvider) : IMailDiagnosticsService
{
    internal const int CurrentHelperVersion = 2;
    internal static readonly TimeSpan CertificateWarning = TimeSpan.FromDays(14);
    private const int MaxDomainsChecked = 20;

    public async Task<MailDiagnosticsDto> GetAsync(int serverId, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var state = await inventory.GetStateAsync(serverId, ct).ConfigureAwait(false);
        if (state is null)
            return new MailDiagnosticsDto { CheckedAt = now, Items = [Item("installed", MailCheckVerdict.Missing)] };

        var server = await serverRepo.FindServerAsync(serverId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Server {serverId} not found.");
        var items = new List<MailDiagnosticItemDto>
        {
            Item("installed", MailCheckVerdict.Ok),
            server.MailSetupAvailable ? Item("capability", MailCheckVerdict.Ok) : Item("capability", MailCheckVerdict.Missing, "enable-mail-setup"),
            state.HelperVersion >= CurrentHelperVersion
                ? Item("helper-version", MailCheckVerdict.Ok, state.HelperVersion.ToString(CultureInfo.InvariantCulture))
                : Item("helper-version", MailCheckVerdict.Mismatch, state.HelperVersion.ToString(CultureInfo.InvariantCulture)),
            Services(state),
            SpamFilter(state),
            Certificate(state, now),
            state.QueueSize == 0
                ? Item("queue", MailCheckVerdict.Ok)
                : Item("queue", MailCheckVerdict.Mismatch, state.QueueSize.ToString(CultureInfo.InvariantCulture))
        };
        items.Add(await HostnameAsync(state.Hostname, server.IpAddress, ct).ConfigureAwait(false));
        items.Add(await ReverseDnsAsync(state.Hostname, server.IpAddress, ct).ConfigureAwait(false));
        items.AddRange(await MxAsync(serverId, state.Hostname, ct).ConfigureAwait(false));
        return new MailDiagnosticsDto { CheckedAt = now, Items = items };
    }

    public async Task<MailDnsCheckDto> VerifyDomainDnsAsync(int serverId, int domainId, CancellationToken ct = default)
    {
        var domain = await inventory.GetTrackedDomainAsync(serverId, domainId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException($"Mail domain {domainId} not found.");
        var state = await inventory.GetStateAsync(serverId, ct).ConfigureAwait(false);
        var result = await verifier.VerifyAsync(domain, state?.Hostname, ct).ConfigureAwait(false);
        await inventory.SaveChangesAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<MailMxPreviewDto> PreviewMxAsync(string domain, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Length > 253 || !MailValidation.IsValidDomainName(domain))
            throw new BadRequestException("Invalid mail domain.");

        try
        {
            var hosts = await resolver.MxAsync(domain, ct).ConfigureAwait(false);
            if (hosts.Count > 20)
                return new MailMxPreviewDto { Domain = domain, Verdict = MailCheckVerdict.Unknown };
            return new MailMxPreviewDto
            {
                Domain = domain,
                Hosts = hosts.ToList(),
                Verdict = hosts.Count == 0 ? MailCheckVerdict.Missing : MailCheckVerdict.Ok
            };
        }
        catch (MailDnsLookupException)
        {
            return new MailMxPreviewDto { Domain = domain, Verdict = MailCheckVerdict.Unknown };
        }
    }

    private static MailDiagnosticItemDto Services(MailState state)
    {
        var stopped = new[] { ("postfix", state.IsPostfixRunning), ("dovecot", state.IsDovecotRunning), ("opendkim", state.IsOpenDkimRunning) }
            .Where(s => !s.Item2)
            .Select(s => s.Item1)
            .ToList();
        return stopped.Count == 0
            ? Item("services", MailCheckVerdict.Ok)
            : Item("services", MailCheckVerdict.Mismatch, string.Join(",", stopped));
    }

    private static MailDiagnosticItemDto SpamFilter(MailState state) => (state.IsSpamFilterInstalled, state.IsSpamFilterRunning) switch
    {
        (true, true) => Item("spam-filter", MailCheckVerdict.Ok, state.SpamFilterName),
        (true, false) => Item("spam-filter", MailCheckVerdict.Mismatch, "stopped"),
        _ => Item("spam-filter", MailCheckVerdict.Missing)
    };

    private static MailDiagnosticItemDto Certificate(MailState state, DateTime now)
    {
        if (string.IsNullOrEmpty(state.TlsCertPath)) return Item("tls", MailCheckVerdict.Missing);
        if (!state.TlsIsReadable) return Item("tls", MailCheckVerdict.Unknown, "unreadable");
        if (state.TlsIsSelfSigned) return Item("tls", MailCheckVerdict.Mismatch, "self-signed");
        if (state.TlsExpiresAt is { } expires && expires - now < CertificateWarning)
            return Item("tls", MailCheckVerdict.Mismatch, expires <= now ? "expired" : "expiring");
        return Item("tls", MailCheckVerdict.Ok);
    }

    private async Task<MailDiagnosticItemDto> HostnameAsync(string hostname, string serverIp, CancellationToken ct)
    {
        if (!MailValidation.IsValidDomainName(hostname)) return Item("hostname-dns", MailCheckVerdict.Unknown, "no-hostname");
        try
        {
            var addresses = await resolver.AddressesAsync(hostname, ct).ConfigureAwait(false);
            if (addresses.Count == 0) return Item("hostname-dns", MailCheckVerdict.Missing, hostname);
            return addresses.Contains(serverIp)
                ? Item("hostname-dns", MailCheckVerdict.Ok, hostname)
                : Item("hostname-dns", MailCheckVerdict.Mismatch, $"{hostname}: {string.Join(", ", addresses)}");
        }
        catch (MailDnsLookupException)
        {
            return Item("hostname-dns", MailCheckVerdict.Unknown, "resolver-error");
        }
    }

    private async Task<MailDiagnosticItemDto> ReverseDnsAsync(string hostname, string serverIp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(serverIp)) return Item("reverse-dns", MailCheckVerdict.Unknown, "no-address");
        try
        {
            var names = await resolver.PtrAsync(serverIp, ct).ConfigureAwait(false);
            if (names.Count == 0) return Item("reverse-dns", MailCheckVerdict.Missing, serverIp);
            return names.Contains(hostname, StringComparer.OrdinalIgnoreCase)
                ? Item("reverse-dns", MailCheckVerdict.Ok, serverIp)
                : Item("reverse-dns", MailCheckVerdict.Mismatch, $"{serverIp}: {string.Join(", ", names)}");
        }
        catch (MailDnsLookupException)
        {
            return Item("reverse-dns", MailCheckVerdict.Unknown, "resolver-error");
        }
    }

    private async Task<List<MailDiagnosticItemDto>> MxAsync(int serverId, string hostname, CancellationToken ct)
    {
        var domains = (await mailRepo.GetDomainsAsync(serverId, ct).ConfigureAwait(false))
            .Where(d => d.IsActive)
            .Take(MaxDomainsChecked)
            .ToList();
        var items = new List<MailDiagnosticItemDto>();
        foreach (var domain in domains)
        {
            var expected = MailDnsRecordFactory.MxHost(domain, hostname);
            try
            {
                var (verdict, _, observed) = MailDnsVerifier.EvaluateMx(await resolver.MxAsync(domain.Name, ct).ConfigureAwait(false), expected);
                items.Add(Item($"mx:{domain.Name}", verdict, observed.Count == 0 ? expected : string.Join(", ", observed)));
            }
            catch (MailDnsLookupException)
            {
                items.Add(Item($"mx:{domain.Name}", MailCheckVerdict.Unknown, "resolver-error"));
            }
        }
        return items;
    }

    private static MailDiagnosticItemDto Item(string key, MailCheckVerdict verdict, string detail = "") =>
        new() { Key = key, Verdict = verdict, Detail = detail };
}
