using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddPipelineSourceAndRunIdempotency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SourceRepositoryId",
            table: "Pipelines",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "IdempotencyKey",
            table: "PipelineRuns",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RepositoryUrl",
            table: "PipelineRuns",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        // Existing project pipelines are safe to bind automatically only when their project has
        // exactly one repository. Multi-repository projects remain null and must make an explicit
        // choice; silently selecting one would recreate the wrong-workspace production failure.
        // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: additive deterministic backfill binds only projects
        // with exactly one repository; ambiguous multi-repository projects intentionally remain null.
        migrationBuilder.Sql("""
            UPDATE "Pipelines" AS p
            SET "SourceRepositoryId" = repositories."Id"
            FROM (
                SELECT MIN("Id") AS "Id", "ProjectId"
                FROM "GitInternalRepos"
                GROUP BY "ProjectId"
                HAVING COUNT(*) = 1
            ) AS repositories
            WHERE p."ProjectId" = repositories."ProjectId"
              AND p."SourceRepositoryId" IS NULL;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Pipelines_SourceRepositoryId",
            table: "Pipelines",
            column: "SourceRepositoryId");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineRuns_PipelineId_IdempotencyKey",
            table: "PipelineRuns",
            columns: new[] { "PipelineId", "IdempotencyKey" },
            unique: true,
            filter: "\"IdempotencyKey\" IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "FK_Pipelines_GitInternalRepos_SourceRepositoryId",
            table: "Pipelines",
            column: "SourceRepositoryId",
            principalTable: "GitInternalRepos",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Pipelines_GitInternalRepos_SourceRepositoryId",
            table: "Pipelines");

        migrationBuilder.DropIndex(
            name: "IX_Pipelines_SourceRepositoryId",
            table: "Pipelines");

        migrationBuilder.DropIndex(
            name: "IX_PipelineRuns_PipelineId_IdempotencyKey",
            table: "PipelineRuns");

        migrationBuilder.DropColumn(
            name: "SourceRepositoryId",
            table: "Pipelines");

        migrationBuilder.DropColumn(
            name: "IdempotencyKey",
            table: "PipelineRuns");

        migrationBuilder.DropColumn(
            name: "RepositoryUrl",
            table: "PipelineRuns");
    }
}
