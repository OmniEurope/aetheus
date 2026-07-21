// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.AppMonitoring;

/// <summary>One parsed error occurrence to fold into a fingerprint group.</summary>
public readonly record struct AppErrorUpsert(string Fingerprint, string ExceptionType, string Message, string? TopFrame, DateTime At);

public interface IAppErrorRepository
{
    /// <summary>Folds a batch of error occurrences into fingerprint groups (insert or increment). Returns groups touched.</summary>
    Task<int> UpsertErrorsAsync(int appId, IReadOnlyCollection<AppErrorUpsert> errors, CancellationToken ct = default);
    Task<(List<AppErrorEvent> Items, int TotalCount)> GetErrorsAsync(int appId, int page, int pageSize, CancellationToken ct = default);
    Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
}
