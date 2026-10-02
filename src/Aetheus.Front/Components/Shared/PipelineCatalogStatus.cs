// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Recette R-122: the states the pipelines summary strip counts and filters on. Every one is derived
/// from data the catalogue already holds (the newest run of <see cref="PipelineDependencyDto.RecentRuns"/>
/// and the fleet freshness), so the strip costs no request of its own.
/// </summary>
public enum PipelineCatalogStatus
{
    /// <summary>The newest run failed.</summary>
    Failed,

    /// <summary>The newest run has not finished: pending, running or waiting for an approval.</summary>
    Running,

    /// <summary>The pipeline has no run at all.</summary>
    NeverRun,

    /// <summary>The pipeline is pinned to an older version of its model (fleet data only).</summary>
    Outdated,

    /// <summary>The pipeline runs the latest version of its model (fleet data only, recette R-167).</summary>
    Current,

    /// <summary>The pipeline follows no model of the catalogue (fleet data only, recette R-167).</summary>
    OffCatalog
}
