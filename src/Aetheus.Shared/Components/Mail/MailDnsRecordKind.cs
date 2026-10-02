// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Mail;

/// <summary>DNS records checked for a mail domain.</summary>
public enum MailDnsRecordKind
{
    Mx,
    Spf,
    Dkim,
    Dmarc
}
