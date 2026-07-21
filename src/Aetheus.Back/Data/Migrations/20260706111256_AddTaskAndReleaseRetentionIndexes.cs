using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddTaskAndReleaseRetentionIndexes : Migration
{
    // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: retries additive concurrent indexes after removing only invalid leftovers.
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // F-INF-08: on PostgreSQL build these with CREATE INDEX CONCURRENTLY so applying the migration
        // at startup does NOT take a write lock on the large, hot Tasks table (a plain CREATE INDEX
        // would block writes for the whole build and stall the deploy health-gate). CONCURRENTLY cannot
        // run inside a transaction, hence suppressTransaction: true.
        // A CONCURRENTLY build that is interrupted (timeout/kill) leaves an INVALID index carrying the
        // name; a bare "IF NOT EXISTS" retry would then see the name, skip, and leave the invalid index
        // forever. So DROP INDEX CONCURRENTLY IF EXISTS first (no-op on a clean first apply; removes the
        // invalid leftover when EF retries a partially-failed migration), then re-create.
        if (migrationBuilder.ActiveProvider?.Contains("Npgsql", System.StringComparison.OrdinalIgnoreCase) == true)
        {
            foreach (var (name, table, cols) in new[]
            {
                ("IX_Tasks_Status_CreatedAt", "Tasks", @"""Status"", ""CreatedAt"""),
                ("IX_Tasks_Status_StartedAt", "Tasks", @"""Status"", ""StartedAt"""),
                ("IX_Releases_PublishedAt", "Releases", @"""PublishedAt"""),
            })
            {
                migrationBuilder.Sql($@"DROP INDEX CONCURRENTLY IF EXISTS ""{name}"";", suppressTransaction: true);
                migrationBuilder.Sql($@"CREATE INDEX CONCURRENTLY IF NOT EXISTS ""{name}"" ON ""{table}"" ({cols});", suppressTransaction: true);
            }
            return;
        }

        // Non-Npgsql providers (none in prod; kept for provider-agnostic tooling) use the standard path.
        migrationBuilder.CreateIndex(
            name: "IX_Tasks_Status_CreatedAt",
            table: "Tasks",
            columns: new[] { "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_Status_StartedAt",
            table: "Tasks",
            columns: new[] { "Status", "StartedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Releases_PublishedAt",
            table: "Releases",
            column: "PublishedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Tasks_Status_CreatedAt",
            table: "Tasks");

        migrationBuilder.DropIndex(
            name: "IX_Tasks_Status_StartedAt",
            table: "Tasks");

        migrationBuilder.DropIndex(
            name: "IX_Releases_PublishedAt",
            table: "Releases");
    }
}
