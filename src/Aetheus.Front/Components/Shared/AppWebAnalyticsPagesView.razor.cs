// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Recette R-355: the "top pages this month" grid of a monitored app, on its own telemetry sub-tab.
/// It reads the same web-analytics summary as <see cref="AppWebAnalyticsView"/>
/// (<see cref="MonitoringApi.GetAppWebAnalyticsAsync"/>); the tabs render only the active panel, so
/// each view fetches when its tab is shown, as the Visitors view already did.
/// </summary>
public partial class AppWebAnalyticsPagesView : AppWebAnalyticsSummaryViewBase;
