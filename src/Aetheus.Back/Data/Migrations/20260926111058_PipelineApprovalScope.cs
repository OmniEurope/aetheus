using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class PipelineApprovalScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: widening only (recette R-370). DROP NOT NULL accepts
            // every row the previous colour writes (it always sets an environment) and changes no stored
            // value; EF would emit it as AlterColumn, which the gate cannot tell from a narrowing, hence
            // the raw statement. The FK and its index are untouched. What the previous colour cannot read
            // is a row without environment: only this version writes one, and only for a pipeline that
            // declares approval_timeout_minutes on a stage without environment. The previous colour's
            // approval reads join the environment (required navigation, inner join), so it does not crash
            // on such rows but cannot see them: a run waiting on one would neither show its approval nor
            // expire there. Before a rollback to the previous colour, cancel the runs waiting on an
            // environment-less approval.
            migrationBuilder.Sql("ALTER TABLE \"PipelineApprovals\" ALTER COLUMN \"EnvironmentId\" DROP NOT NULL;");

            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "PipelineApprovals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: backfill of the column added just above, no schema
            // change. Only a stage's own confirmation window ever stored TimeoutMinutes (an environment
            // approval leaves it null and uses the environment's delay), so those rows are the pipeline's
            // approvals (Scope 1). Without it, a run waiting on one across the upgrade would be asked
            // again for the same stage. The previous colour never reads Scope; a confirmation it raises
            // after this ran keeps Scope 0, so at worst that stage is asked once more, never less.
            migrationBuilder.Sql("UPDATE \"PipelineApprovals\" SET \"Scope\" = 1 WHERE \"TimeoutMinutes\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Scope",
                table: "PipelineApprovals");

            // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: Down only. An environment-less approval cannot survive
            // NOT NULL: it goes, with the decision it held.
            migrationBuilder.Sql("DELETE FROM \"PipelineApprovals\" WHERE \"EnvironmentId\" IS NULL;");
            // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: Down only, restores the constraint once no row violates it.
            migrationBuilder.Sql("ALTER TABLE \"PipelineApprovals\" ALTER COLUMN \"EnvironmentId\" SET NOT NULL;");
        }
    }
}
