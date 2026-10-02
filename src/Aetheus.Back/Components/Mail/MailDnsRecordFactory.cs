// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Mail;

/// <summary>
/// PLAN-005: the DNS records a mail domain needs, derived from the server inventory. Shared by the
/// "records to publish" dialog and the DNS verification so both always expect the same values.
/// </summary>
internal static class MailDnsRecordFactory
{
    public const string SpfRecord = "v=spf1 mx a ~all";

    /// <summary>MX target: the Postfix <c>myhostname</c> reported by the agent, else <c>mail.&lt;domain&gt;</c>.</summary>
    public static string MxHost(MailDomain domain, string? hostname) =>
        MailValidation.IsValidDomainName(hostname) ? hostname!.ToLowerInvariant() : $"mail.{domain.Name}";

    public static string DkimRecordName(MailDomain domain) => $"{domain.DkimSelector}._domainkey.{domain.Name}";

    public static string DmarcRecord(MailDomain domain) => $"v=DMARC1; p=quarantine; rua=mailto:postmaster@{domain.Name}";

    public static MailDnsRecordsDto Build(MailDomain domain, string? hostname) => new()
    {
        Domain = domain.Name,
        MxRecord = $"10 {MxHost(domain, hostname)}.",
        SpfRecord = SpfRecord,
        DkimSelector = domain.DkimSelector,
        DkimRecord = DkimRecordName(domain),
        DkimPublicKey = domain.DkimPublicKey,
        DmarcRecord = DmarcRecord(domain)
    };
}
