using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineBuildNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BuildCounter",
                table: "Pipelines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BuildNumber",
                table: "PipelineRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: additive backfill only. Seeds the new counter
            // from each pipeline's existing run count so the first build number issued after this
            // migration continues that pipeline's history instead of restarting at 1. Touches only
            // the column added above, writes no other table, and is idempotent for a fresh install
            // (no runs yields 0). Historical runs keep BuildNumber = 0; nothing reads it for them.
            migrationBuilder.Sql(
                """
                UPDATE "Pipelines" AS p
                SET "BuildCounter" = (
                    SELECT COUNT(*) FROM "PipelineRuns" AS r WHERE r."PipelineId" = p."Id"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuildCounter",
                table: "Pipelines");

            migrationBuilder.DropColumn(
                name: "BuildNumber",
                table: "PipelineRuns");
        }
    }
}
