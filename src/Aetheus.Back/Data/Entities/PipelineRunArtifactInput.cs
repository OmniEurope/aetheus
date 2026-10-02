// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Recette R-366/R-367: one artifact a pipeline run consumed (restored or deployed), recorded when the
/// step is dispatched. It is the only durable trace of a run's inputs: the task environment that
/// carries the artifact id is encrypted and purged with the task. The artifact name, digest and
/// producing run are copied so the record still reads after artifact retention deleted the file.
/// </summary>
public class PipelineRunArtifactInput
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public ArtifactInputKind Kind { get; set; }
    public int? ArtifactId { get; set; }
    public string ArtifactName { get; set; } = string.Empty;
    public string? Sha256 { get; set; }
    /// <summary>The run that produced the artifact (no foreign key: it may be purged first).</summary>
    public int SourcePipelineRunId { get; set; }
    /// <summary>The release the step selected, when the step named one (deploy by release).</summary>
    public int? ReleaseId { get; set; }
    public DateTime RecordedAt { get; set; }

    public PipelineRun PipelineRun { get; set; } = null!;
    public PipelineArtifact? Artifact { get; set; }
    public Release? Release { get; set; }
}
