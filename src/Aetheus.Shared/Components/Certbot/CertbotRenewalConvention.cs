// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Certbot;

/// <summary>
/// How a certificate lineage renews compared with the Aetheus convention (PLAN-007): the ACME web root
/// declared in <c>/etc/letsencrypt/cli.ini</c>. <see cref="Unknown"/> is the default so an agent that
/// predates the field, or cannot read the renewal file, is never reported as conforming.
/// </summary>
public enum CertbotRenewalConvention
{
    Unknown,
    Conforming,
    NotWebroot,
    WrongWebroot
}
