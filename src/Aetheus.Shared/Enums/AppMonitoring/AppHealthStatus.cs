// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

/// <summary>
/// Health state of a monitored application. <see cref="Unknown"/> is a first-class state
/// (no green-by-default rule, ADR-021 no-fake): an app that has never been probed is never Up.
/// </summary>
public enum AppHealthStatus
{
    Unknown = 0,
    Up = 1,
    Down = 2,
    Degraded = 3
}
