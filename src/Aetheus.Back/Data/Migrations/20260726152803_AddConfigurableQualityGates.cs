using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddConfigurableQualityGates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "NewFindingsOnly",
            table: "AnalysisPolicies",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "PolicyKey",
            table: "AnalysisPolicies",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: deterministic, idempotent backfill of the new
        // nullable key; legacy binaries may still insert null until the V+2 contract migration.
        migrationBuilder.Sql("""
            UPDATE "AnalysisPolicies"
            SET "PolicyKey" = 'legacy.' || "Id"::text
            WHERE "PolicyKey" IS NULL;
            """);

        // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: the partial index preserves the rolling-upgrade
        // window for legacy null keys while NULLS NOT DISTINCT enforces global/organization scopes.
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_AnalysisPolicies_OrganizationId_ProjectId_PolicyKey"
            ON "AnalysisPolicies" ("OrganizationId", "ProjectId", "PolicyKey") NULLS NOT DISTINCT
            WHERE "PolicyKey" IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AnalysisPolicies_OrganizationId_ProjectId_PolicyKey",
            table: "AnalysisPolicies");

        migrationBuilder.DropColumn(
            name: "NewFindingsOnly",
            table: "AnalysisPolicies");

        migrationBuilder.DropColumn(
            name: "PolicyKey",
            table: "AnalysisPolicies");
    }
}
