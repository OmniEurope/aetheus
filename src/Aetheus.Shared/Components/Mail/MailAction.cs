// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Mail;

/// <summary>Service, queue and configuration actions of the mail stack. PLAN-005 replaced the former
/// SpamAssassin members (a shell fallback the non-root agent could never run) with rspamd controls.</summary>
public enum MailAction
{
    StartPostfix,
    StopPostfix,
    RestartPostfix,
    ReloadPostfix,
    StartDovecot,
    StopDovecot,
    RestartDovecot,
    ReloadDovecot,
    FlushQueue,
    ViewQueue,
    TestConfig,
    StartSpamFilter,
    StopSpamFilter,
    RestartSpamFilter,
    RestartOpenDkim
}
