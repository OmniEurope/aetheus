// SPDX-License-Identifier: EUPL-1.2
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Aetheus.Back.IntegrationTests.SharedPostgresContainer))]

namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// DRPS: a SINGLE PostgreSQL container shared by the whole assembly, hosting TWO databases -
/// <c>aetheus_it</c> for the relational suites (restored from a migrated template for each test, serialised
/// within <see cref="PostgresCollection"/>) and <c>aetheus_smoke</c> for the additive API-surface
/// sweep (<see cref="ApiSmokeCollection"/>). Because the two collections use DIFFERENT databases, they
/// can still run in parallel without one wiping the other's seed - but only ONE Postgres boots, removing
/// the second concurrent container and its CI cost/flake. The schema reset itself stays in
/// <see cref="AetheusWebApplicationFactory"/> (now opt-in), so each suite controls its own pristine
/// state. Exposed as an xUnit.v3 assembly fixture so both collection fixtures consume the same container.
/// <para>RUNTIME NOTE: needs Docker/Testcontainers to execute - validate the merge on a box with Docker
/// (e.g. via <c>-ti</c>) before relying on it in CI.</para>
/// </summary>
public sealed class SharedPostgresContainer : IAsyncLifetime
{
    private const string ItDatabase = "aetheus_it";
    private const string SmokeDatabase = "aetheus_smoke";

    // Pinned to the exact production multi-platform digest (deploy/compose/remote.compose.yml) so tests
    // exercise the same Postgres, not EF InMemory (which skips relational migrations + FK enforcement).
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
            "postgres:18-alpine@sha256:9a8afca54e7861fd90fab5fdf4c42477a6b1cb7d293595148e674e0a3181de15")
        .WithDatabase(ItDatabase)
        .WithUsername("aetheus")
        .WithPassword("aetheus_shared_pwd")
        .Build();

    /// <summary>Connection string for the relational-suite database (<c>aetheus_it</c>).</summary>
    public string ItConnectionString => _container.GetConnectionString();

    /// <summary>Connection string for the API-smoke database (<c>aetheus_smoke</c>) in the same container.</summary>
    public string SmokeConnectionString =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = SmokeDatabase }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Migrate once, then let every relational test clone this immutable database. PostgreSQL's
        // native template copy is substantially cheaper than replaying the complete EF migration chain.
        await PostgresDatabaseTemplate.InitializeAsync(_container.GetConnectionString());
        await PostgresDatabaseTemplate.ResetAsync(_container.GetConnectionString());

        // Create the second database in the same instance so the smoke suite never shares a schema with
        // the relational suites - no cross-suite wipe despite a single container.
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE {SmokeDatabase};";
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
