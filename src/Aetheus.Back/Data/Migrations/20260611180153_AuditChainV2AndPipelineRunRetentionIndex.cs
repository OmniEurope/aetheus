using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AuditChainV2AndPipelineRunRetentionIndex : Migration
{
    // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: additive partial unique index required by PostgreSQL semantics.
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_PipelineRuns_StartedAt",
            table: "PipelineRuns",
            column: "StartedAt");

        // F-014: partial unique index prevents audit-chain forks across concurrent instances.
        // Excludes the empty-string seed hash (the very first entry) so the initial row is
        // insertable without violating uniqueness.
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_AuditLogs_PreviousHash_Unique"
                ON "AuditLogs" ("PreviousHash")
                WHERE "PreviousHash" <> '';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PipelineRuns_StartedAt",
            table: "PipelineRuns");

        migrationBuilder.DropIndex(
            name: "IX_AuditLogs_PreviousHash_Unique",
            table: "AuditLogs");
    }
}
