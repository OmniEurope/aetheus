// SPDX-License-Identifier: EUPL-1.2
using System.Diagnostics.CodeAnalysis;
using Aetheus.Back.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aetheus.Back.IntegrationTests;

internal static class PostgresDatabaseTemplate
{
    private const string TemplateDatabase = "aetheus_it_template";

    public static async Task InitializeAsync(string connectionString)
    {
        var templateConnectionString = WithDatabase(connectionString, TemplateDatabase);
        await ExecuteAdminAsync(connectionString,
            $"DROP DATABASE IF EXISTS {TemplateDatabase} WITH (FORCE); CREATE DATABASE {TemplateDatabase};");

        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(templateConnectionString)
            .Options))
        {
            await db.Database.MigrateAsync();
        }

        NpgsqlConnection.ClearAllPools();
    }

    public static async Task ResetAsync(string connectionString)
    {
        NpgsqlConnection.ClearAllPools();
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        EnsureSafeDatabaseName(database);
        await ExecuteAdminAsync(connectionString,
            $"DROP DATABASE IF EXISTS {database} WITH (FORCE); CREATE DATABASE {database} TEMPLATE {TemplateDatabase};");
    }

    // CA2100 sees an interpolated database name in DDL. PostgreSQL does not accept a parameter for
    // an identifier, so this cannot be bound; EnsureSafeDatabaseName above is the guard instead, and
    // it refuses anything outside [A-Za-z0-9_] before the name reaches the statement.
    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "DDL identifier cannot be a parameter; EnsureSafeDatabaseName restricts it to [A-Za-z0-9_].")]
    public static void Reset(string connectionString)
    {
        NpgsqlConnection.ClearAllPools();
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        EnsureSafeDatabaseName(database);
        using var connection = new NpgsqlConnection(WithDatabase(connectionString, "postgres"));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"DROP DATABASE IF EXISTS {database} WITH (FORCE); CREATE DATABASE {database} TEMPLATE {TemplateDatabase};";
        command.ExecuteNonQuery();
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Test-only admin helper; every caller in this file passes a literal statement.")]
    private static async Task ExecuteAdminAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(WithDatabase(connectionString, "postgres"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    private static void EnsureSafeDatabaseName(string? database)
    {
        if (string.IsNullOrWhiteSpace(database) || database.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidOperationException($"Unsafe integration database name: '{database}'.");
    }
}
