// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Components.AppMonitoring;

/// <summary>
/// Stored as an integer (no check constraint). Day, Week and Month key both the visitor identities and
/// the aggregates. The Authenticated* kinds key only visitor identities, on the event's authenticated
/// pseudonym: they decide when a period's AuthenticatedUniqueVisitors grows, and never carry an
/// aggregate row of their own nor feed any other counter.
/// </summary>
public enum AnalyticsPeriodKind
{
    Day = 1,
    Week = 2,
    Month = 3,
    AuthenticatedDay = 11,
    AuthenticatedWeek = 12,
    AuthenticatedMonth = 13
}
