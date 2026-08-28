// SPDX-License-Identifier: EUPL-1.2
using System.Data;
using Npgsql;

namespace Aetheus.Back.Services;

public interface IPostgresLeaderLease
{
    Task RunAsLeaderAsync(
        string leaseName,
        Func<CancellationToken, Task> leaderWork,
        CancellationToken stoppingToken);

    Task RunSerializedAsync(
        string operationName,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);
}

/// <summary>
/// Holds a PostgreSQL session advisory lock while one background worker is active. During
/// blue-green overlap only one backend therefore schedules, reconciles, or prunes data.
/// A heartbeat detects a severed lease connection and cancels the leader work fail-closed.
/// </summary>
public sealed class PostgresLeaderLease : IPostgresLeaderLease
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgresLeaderLease> _logger;
    private readonly TimeSpan _retryDelay;

    public PostgresLeaderLease(
        IConfiguration configuration,
        ILogger<PostgresLeaderLease> logger)
        : this(configuration, logger, RetryDelay)
    {
    }

    internal PostgresLeaderLease(
        IConfiguration configuration,
        ILogger<PostgresLeaderLease> logger,
        TimeSpan retryDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retryDelay, TimeSpan.Zero);
        _configuration = configuration;
        _logger = logger;
        _retryDelay = retryDelay;
    }

    public async Task RunSerializedAsync(
        string operationName,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(operation);

        var connectionString = _configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required for operation locks.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var acquire = new NpgsqlCommand(
            "SELECT pg_advisory_lock(hashtextextended(@name, 0))", connection))
        {
            acquire.Parameters.AddWithValue("name", operationName);
            await acquire.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ReleaseLeaseAsync(connection, operationName).ConfigureAwait(false);
        }
    }

    public async Task RunAsLeaderAsync(
        string leaseName,
        Func<CancellationToken, Task> leaderWork,
        CancellationToken stoppingToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);
        ArgumentNullException.ThrowIfNull(leaderWork);

        var connectionString = _configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required for hosted-service leader leases.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken).ConfigureAwait(false);

                await using var acquire = new NpgsqlCommand(
                    "SELECT pg_try_advisory_lock(hashtextextended(@name, 0))", connection);
                acquire.Parameters.AddWithValue("name", leaseName);
                var acquired = (bool)(await acquire.ExecuteScalarAsync(stoppingToken).ConfigureAwait(false) ?? false);
                if (!acquired)
                {
                    await Task.Delay(_retryDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                _logger.LogInformation("Acquired PostgreSQL leader lease {LeaseName}", leaseName);
                using var leaseLost = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var work = leaderWork(leaseLost.Token);
                try
                {
                    while (!work.IsCompleted)
                    {
                        await Task.Delay(HeartbeatInterval, stoppingToken).ConfigureAwait(false);
                        await using var heartbeat = new NpgsqlCommand("SELECT 1", connection);
                        await heartbeat.ExecuteScalarAsync(stoppingToken).ConfigureAwait(false);
                    }

                    await work.ConfigureAwait(false);
                    return;
                }
                catch
                {
                    leaseLost.Cancel();
                    try { await work.ConfigureAwait(false); }
                    catch (OperationCanceledException) when (leaseLost.IsCancellationRequested) { }
                    throw;
                }
                finally
                {
                    await ReleaseLeaseAsync(connection, leaseName).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogCritical(
                    ex,
                    "Leader worker {LeaseName} failed; its lease was released and the worker will restart without stopping the backend",
                    leaseName);
                await Task.Delay(_retryDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReleaseLeaseAsync(NpgsqlConnection connection, string leaseName)
    {
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                NpgsqlConnection.ClearPool(connection);
                return;
            }

            using var releaseTimeout = new CancellationTokenSource(ReleaseTimeout);
            await using var release = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtextextended(@name, 0))", connection);
            release.Parameters.AddWithValue("name", leaseName);
            var released = (bool)(await release.ExecuteScalarAsync(releaseTimeout.Token).ConfigureAwait(false) ?? false);
            if (released)
                _logger.LogInformation("Released PostgreSQL leader lease {LeaseName}", leaseName);
            else
                _logger.LogWarning("PostgreSQL session did not own leader lease {LeaseName} during release", leaseName);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            // Returning a session with an advisory lock to the pool can wedge leadership indefinitely.
            // If the explicit unlock cannot be proven, invalidate the pool so this physical session is
            // closed on disposal and PostgreSQL releases all of its session-scoped advisory locks.
            NpgsqlConnection.ClearPool(connection);
            _logger.LogWarning(ex, "Could not explicitly release PostgreSQL leader lease {LeaseName}; invalidated its connection pool", leaseName);
        }
    }
}
