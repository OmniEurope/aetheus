// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

/// <summary>
/// Audit R2-016 follow-up: one measurement of the physical artifact volume. The growth alert needs a
/// measurement at least 24 hours old; kept in the database, it survives deploys and leader changes. Only
/// the measurements of the last 24 hours and the newest one older than that are kept.
/// </summary>
public class ArtifactStorageMeasurement
{
    public long Id { get; set; }
    public DateTime MeasuredAtUtc { get; set; }
    public long Bytes { get; set; }
}
