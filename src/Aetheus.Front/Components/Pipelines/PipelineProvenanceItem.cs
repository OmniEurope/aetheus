// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Pipelines;

internal sealed record PipelineProvenanceItem(
    string Path,
    PipelineElementProvenance Provenance);
