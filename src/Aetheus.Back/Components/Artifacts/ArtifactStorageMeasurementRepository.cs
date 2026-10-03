// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.Artifacts;

public sealed class ArtifactStorageMeasurementRepository(AppDbContext db) : IArtifactStorageMeasurementRepository
{
    public async Task<List<(DateTime At, long Bytes)>> GetAsync(CancellationToken ct = default)
    {
        var rows = await db.ArtifactStorageMeasurements.AsNoTracking()
            .OrderBy(item => item.MeasuredAtUtc).ThenBy(item => item.Id)
            .Select(item => new { item.MeasuredAtUtc, item.Bytes })
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(item => (item.MeasuredAtUtc, item.Bytes))];
    }

    public async Task RecordAsync(
        DateTime measuredAtUtc, long bytes, DateTime windowStartUtc, CancellationToken ct = default)
    {
        db.ArtifactStorageMeasurements.Add(new ArtifactStorageMeasurement
        {
            MeasuredAtUtc = measuredAtUtc,
            Bytes = bytes
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var reference = await db.ArtifactStorageMeasurements.AsNoTracking()
            .Where(item => item.MeasuredAtUtc <= windowStartUtc)
            .OrderByDescending(item => item.MeasuredAtUtc)
            .Select(item => (DateTime?)item.MeasuredAtUtc)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (reference is not { } keptFrom) return;

        var superseded = db.ArtifactStorageMeasurements.Where(item => item.MeasuredAtUtc < keptFrom);
        if (db.Database.IsRelational())
        {
            await superseded.ExecuteDeleteAsync(ct).ConfigureAwait(false);
            return;
        }
        db.ArtifactStorageMeasurements.RemoveRange(await superseded.ToListAsync(ct).ConfigureAwait(false));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
