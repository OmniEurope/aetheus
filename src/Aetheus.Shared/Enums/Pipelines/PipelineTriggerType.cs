// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

public enum PipelineTriggerType
{
    Manual = 0,
    Webhook = 1,
    Schedule = 2,

    /// <summary>Triggered automatically when an upstream pipeline run succeeds (chaining). Declared via
    /// the upstream pipeline's <c>on_success:</c> list; the downstream run receives the upstream
    /// context (<c>UPSTREAM_RUN_ID</c>, <c>UPSTREAM_RELEASE</c>) as variables.</summary>
    Downstream = 3
}
