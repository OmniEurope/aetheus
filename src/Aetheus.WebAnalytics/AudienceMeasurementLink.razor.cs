// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Components;

namespace Aetheus.WebAnalytics;

public class AudienceMeasurementLinkBase : ComponentBase
{
    [Inject] protected AetheusWebAnalyticsOptions Options { get; set; } = default!;
}
