using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddAlertRuleProvisioningKey : Migration
{
    // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: deterministic data backfill for the newly added provisioning key.
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProvisioningKey",
            table: "AlertRules",
            type: "text",
            nullable: true);

        migrationBuilder.Sql(
            """
            WITH candidates AS (
                SELECT
                    "Id",
                    CASE
                        WHEN "Metric" = 2 AND "Operator" = 2 AND "SustainedSeconds" = 300
                            THEN 'storage-disk-warning:' || "ServerId"
                        WHEN "Metric" = 2 AND "Operator" = 2 AND "SustainedSeconds" = 60
                            THEN 'storage-disk-critical:' || "ServerId"
                        WHEN "Metric" = 3 AND "Operator" = 1 AND "SustainedSeconds" = 60
                            THEN 'storage-disk-free:' || "ServerId"
                    END AS provisioning_key
                FROM "AlertRules"
                WHERE "ProvisioningKey" IS NULL
                  AND "ServerId" IS NOT NULL
                  AND "Name" LIKE 'Stockage%'
            ), ranked AS (
                SELECT
                    "Id",
                    provisioning_key,
                    ROW_NUMBER() OVER (PARTITION BY provisioning_key ORDER BY "Id") AS candidate_rank
                FROM candidates
                WHERE provisioning_key IS NOT NULL
            )
            UPDATE "AlertRules" AS rules
            SET "ProvisioningKey" = ranked.provisioning_key
            FROM ranked
            WHERE rules."Id" = ranked."Id"
              AND ranked.candidate_rank = 1;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_AlertRules_ProvisioningKey",
            table: "AlertRules",
            column: "ProvisioningKey",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AlertRules_ProvisioningKey",
            table: "AlertRules");

        migrationBuilder.DropColumn(
            name: "ProvisioningKey",
            table: "AlertRules");
    }
}
