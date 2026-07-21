// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

/// <summary>
/// Outcome of a real connectivity/credential probe against a service connection's provider.
/// There is no "assumed valid": a provider we cannot probe yields <see cref="Unsupported"/>,
/// a failed probe yields <see cref="Error"/> - never a fabricated green.
/// </summary>
public enum ServiceConnectionTestStatus
{
    Valid = 0,
    Invalid = 1,
    Unsupported = 2,
    Error = 3
}
