// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Mail;

/// <summary>Origin of a mail domain, account or alias row. Persisted as an integer: never renumber.</summary>
public enum MailRecordSource
{
    /// <summary>Created from the Aetheus interface.</summary>
    Aetheus = 0,

    /// <summary>Imported from an existing server configuration by the heartbeat reconciliation.</summary>
    Adopted = 1
}
