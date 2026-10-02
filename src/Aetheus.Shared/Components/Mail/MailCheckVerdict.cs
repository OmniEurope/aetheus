// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.Mail;

/// <summary>Outcome of one mail DNS or diagnostic check.</summary>
public enum MailCheckVerdict
{
    /// <summary>The observed value satisfies the expectation.</summary>
    Ok,

    /// <summary>No record or no value was found.</summary>
    Missing,

    /// <summary>A value exists but does not match what the server needs.</summary>
    Mismatch,

    /// <summary>The check could not run (resolver failure, data not collected yet).</summary>
    Unknown
}
