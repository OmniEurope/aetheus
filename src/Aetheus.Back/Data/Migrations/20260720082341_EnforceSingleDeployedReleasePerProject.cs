using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class EnforceSingleDeployedReleasePerProject : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: normalize legacy duplicates before enforcing the invariant; no data is removed.
        migrationBuilder.Sql(
            """
            WITH ranked AS (
                SELECT "Id",
                       ROW_NUMBER() OVER (
                           PARTITION BY "ProjectId"
                           ORDER BY "PublishedAt" DESC NULLS LAST, "Id" DESC) AS row_number
                FROM "Releases"
                WHERE "Status" = 6
            )
            UPDATE "Releases"
            SET "Status" = 2
            WHERE "Id" IN (SELECT "Id" FROM ranked WHERE row_number > 1);
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Releases_ProjectId",
            table: "Releases",
            column: "ProjectId",
            unique: true,
            filter: "\"Status\" = 6");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Releases_ProjectId",
            table: "Releases");
    }
}
