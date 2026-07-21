// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Shared.Enums;

/// <summary>How a pipeline stage selects the server it runs on (resolution precedence:
/// Pool &gt; Environment &gt; Agent). Used by the pre-flight check to label the unresolved target.</summary>
public enum PreflightTargetKind
{
    Agent,
    Pool,
    Environment
}
