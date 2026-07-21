// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

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
    StartSpamAssassin,
    StopSpamAssassin,
    RestartSpamAssassin
}
