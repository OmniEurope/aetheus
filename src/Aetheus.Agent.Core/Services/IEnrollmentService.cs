// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Agent.Core.Services;

public interface IEnrollmentService
{
    bool IsEnrolled { get; }
    Task<bool> EnrollAsync(CancellationToken ct = default);
    Task LoadPersistedCredentialsAsync(CancellationToken ct = default);

    /// <summary>Persists a server-rolled Bearer token to secure storage (same protection as enrollment).</summary>
    Task PersistRenewedTokenAsync(string bearerToken, CancellationToken ct = default);
}
