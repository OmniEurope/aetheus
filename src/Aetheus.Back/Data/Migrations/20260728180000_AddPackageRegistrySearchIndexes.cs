// SPDX-License-Identifier: EUPL-1.2
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

public sealed partial class AddPackageRegistrySearchIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: installs an optional PostgreSQL extension and adds
        // search indexes only; package rows and existing schema contracts remain unchanged.
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        migrationBuilder.Sql(
            """
            CREATE INDEX "IX_RegistryPackages_Name_Trgm"
            ON "RegistryPackages" USING gin ("Name" gin_trgm_ops);
            """);
        migrationBuilder.Sql(
            """
            CREATE INDEX "IX_RegistryPackages_Description_Trgm"
            ON "RegistryPackages" USING gin ("Description" gin_trgm_ops);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_RegistryPackages_Description_Trgm";""");
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_RegistryPackages_Name_Trgm";""");
    }
}
