// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.IntegrationTests;

/// <summary>
/// Relational-suite database (<c>aetheus_it</c>) on the shared assembly container. DRPS: the
/// container itself is owned by <see cref="SharedPostgresContainer"/> (one Postgres for the whole
/// assembly) - this collection fixture just exposes the relational connection string. Pinned to the
/// production <c>postgres:18-alpine</c> engine, so the tests exercise the same Postgres, not EF InMemory
/// (which silently skips relational migrations and FK enforcement).
/// </summary>
public sealed class PostgresFixture(SharedPostgresContainer shared) : IAsyncLifetime
{
    public string ConnectionString => shared.ItConnectionString;

    public Task ResetAsync() => PostgresDatabaseTemplate.ResetAsync(ConnectionString);

    // The container's lifetime is the assembly fixture's; nothing to start/stop per collection.
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres collection";
}
