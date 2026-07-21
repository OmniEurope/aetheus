// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Pages.Pipelines;

internal sealed record PipelineProvenanceItem(
    string Path,
    PipelineElementProvenance Provenance);
