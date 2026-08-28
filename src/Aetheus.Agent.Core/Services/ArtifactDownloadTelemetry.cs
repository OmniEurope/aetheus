// SPDX-License-Identifier: EUPL-1.2
using System.Runtime.CompilerServices;

namespace Aetheus.Agent.Core.Services;

internal sealed record ArtifactDownloadMeasurement(
    string Sha256,
    long Bytes,
    TimeSpan DownloadDuration,
    TimeSpan HashDuration);

internal static class ArtifactDownloadTelemetry
{
    private static readonly ConditionalWeakTable<Stream, ArtifactDownloadMeasurement> Measurements = new();

    internal static void Register(Stream stream, ArtifactDownloadMeasurement measurement) =>
        Measurements.Add(stream, measurement);

    internal static bool TryGet(Stream stream, out ArtifactDownloadMeasurement measurement) =>
        Measurements.TryGetValue(stream, out measurement!);
}
