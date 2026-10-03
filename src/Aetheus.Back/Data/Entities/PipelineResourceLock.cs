// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;

namespace Aetheus.Back.Data.Entities;

/// <summary>
/// A resource one pipeline run holds alone: today an environment (<c>environment:{id}</c>). The key
/// is the primary key, so two runs can never both hold it (decision of 2026-10-02: a run that acts on
/// an environment locks it, the others wait). The lock counts only while its run is still active;
/// a finished, failed, cancelled or timed-out holder no longer blocks anyone, whether or not the row
/// was removed yet, so a lock cannot outlive the run that took it.
/// </summary>
public class PipelineResourceLock
{
    [MaxLength(MaxKeyLength)]
    public string Key { get; set; } = string.Empty;

    public int PipelineRunId { get; set; }

    public DateTime AcquiredAt { get; set; }

    public PipelineRun PipelineRun { get; set; } = null!;

    public const int MaxKeyLength = 200;
}
