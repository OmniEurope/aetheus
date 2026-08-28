// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Components.AppMonitoring.Ingest;

public interface IVisitorIngestService
{
    Task RecordAsync(int appId, string visitorId, CancellationToken ct = default);
}
