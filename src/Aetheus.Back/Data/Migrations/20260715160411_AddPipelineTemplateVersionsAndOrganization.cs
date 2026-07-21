using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddPipelineTemplateVersionsAndOrganization : Migration
{
    // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: seed/backfill and additive case-insensitive index before the legacy contract.
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                invalid_ids text;
                duplicate_names text;
            BEGIN
                SELECT string_agg("Id"::text, ', ' ORDER BY "Id") INTO invalid_ids
                FROM "PipelineTemplates"
                WHERE length("Name") > 100
                   OR length("Description") > 500
                   OR length("Category") > 50
                   OR length("YamlContent") > 50000;
                IF invalid_ids IS NOT NULL THEN
                    RAISE EXCEPTION 'Pipeline template migration blocked: IDs % exceed the new field limits.', invalid_ids;
                END IF;

                SELECT string_agg(name_key, ', ' ORDER BY name_key) INTO duplicate_names
                FROM (
                    SELECT lower("Name") AS name_key
                    FROM "PipelineTemplates"
                    GROUP BY lower("Name")
                    HAVING count(*) > 1
                ) duplicates;
                IF duplicate_names IS NOT NULL THEN
                    RAISE EXCEPTION 'Pipeline template migration blocked: duplicate names (case-insensitive): %.', duplicate_names;
                END IF;
            END $$;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "Name", table: "PipelineTemplates", type: "character varying(100)",
            maxLength: 100, nullable: false, oldClrType: typeof(string), oldType: "text");
        migrationBuilder.AlterColumn<string>(
            name: "Description", table: "PipelineTemplates", type: "character varying(500)",
            maxLength: 500, nullable: false, oldClrType: typeof(string), oldType: "text");
        migrationBuilder.AlterColumn<string>(
            name: "Category", table: "PipelineTemplates", type: "character varying(50)",
            maxLength: 50, nullable: false, oldClrType: typeof(string), oldType: "text");

        migrationBuilder.AddColumn<int>(
            name: "OrganizationId", table: "PipelineTemplates", type: "integer",
            nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(
            name: "LatestVersion", table: "PipelineTemplates", type: "integer",
            nullable: false, defaultValue: 1);

        migrationBuilder.CreateTable(
            name: "PipelineTemplateVersions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                TemplateId = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                YamlContent = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                ChangelogEntry = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedByUsername = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PipelineTemplateVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_PipelineTemplateVersions_PipelineTemplates_TemplateId",
                    column: x => x.TemplateId,
                    principalTable: "PipelineTemplates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Existing installations are populated before this migration runs. Create/reuse the
        // default organization, preserve the exact current YAML as an immutable version, then
        // remove the legacy mutable columns only after the copy is complete.
        migrationBuilder.Sql("""
            INSERT INTO "Organizations" ("Name", "Slug", "Description", "CreatedAt", "UpdatedAt")
            SELECT 'Aetheus', 'aetheus',
                   'Default organization seeded on first run; owns all bootstrap resources.',
                   now(), now()
            WHERE NOT EXISTS (SELECT 1 FROM "Organizations" WHERE "Slug" = 'aetheus');

            UPDATE "PipelineTemplates" SET "OrganizationId" = organization."Id"
            FROM "Organizations" organization
            WHERE organization."Slug" = 'aetheus' AND "PipelineTemplates"."OrganizationId" = 0;

            UPDATE "PipelineTemplates" SET "LatestVersion" = GREATEST("Version", 1);

            INSERT INTO "PipelineTemplateVersions"
                ("TemplateId", "Version", "YamlContent", "ChangelogEntry", "CreatedAt", "CreatedByUsername")
            SELECT "Id", GREATEST("Version", 1), "YamlContent",
                   LEFT(COALESCE(NULLIF("Changelog", ''), 'Migrated existing template'), 500),
                   "CreatedAt", 'system'
            FROM "PipelineTemplates";

            INSERT INTO "ResourcePermissions" ("RoleId", "ResourceType", "ResourceId", "Permission")
            SELECT role."Id", 10, NULL,
                   CASE role."Name" WHEN 'Admin' THEN 2 WHEN 'Contributor' THEN 1 ELSE 0 END
            FROM "Roles" role
            WHERE role."Name" IN ('Admin', 'Contributor', 'Reader')
              AND NOT EXISTS (
                  SELECT 1 FROM "ResourcePermissions" permission
                  WHERE permission."RoleId" = role."Id"
                    AND permission."ResourceType" = 10
                    AND permission."ResourceId" IS NULL);
            """);

        migrationBuilder.DropColumn(name: "Changelog", table: "PipelineTemplates");
        migrationBuilder.DropColumn(name: "YamlContent", table: "PipelineTemplates");
        migrationBuilder.DropColumn(name: "Version", table: "PipelineTemplates");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineTemplates_OrganizationId_Name",
            table: "PipelineTemplates", columns: new[] { "OrganizationId", "Name" }, unique: true);
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_PipelineTemplates_OrganizationId_Name_CI"
            ON "PipelineTemplates" ("OrganizationId", lower("Name"));
            """);
        migrationBuilder.CreateIndex(
            name: "IX_PipelineTemplateVersions_TemplateId_Version",
            table: "PipelineTemplateVersions", columns: new[] { "TemplateId", "Version" }, unique: true);
        migrationBuilder.AddForeignKey(
            name: "FK_PipelineTemplates_Organizations_OrganizationId",
            table: "PipelineTemplates", column: "OrganizationId",
            principalTable: "Organizations", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM "ResourcePermissions" WHERE "ResourceType" = 10;
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_PipelineTemplates_Organizations_OrganizationId", table: "PipelineTemplates");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PipelineTemplates_OrganizationId_Name_CI\";");
        migrationBuilder.DropIndex(
            name: "IX_PipelineTemplates_OrganizationId_Name", table: "PipelineTemplates");

        migrationBuilder.AddColumn<int>(
            name: "Version", table: "PipelineTemplates", type: "integer", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<string>(
            name: "YamlContent", table: "PipelineTemplates", type: "text", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(
            name: "Changelog", table: "PipelineTemplates", type: "text", nullable: false, defaultValue: "");

        migrationBuilder.Sql("""
            UPDATE "PipelineTemplates" template
            SET "Version" = template."LatestVersion",
                "YamlContent" = version."YamlContent",
                "Changelog" = version."ChangelogEntry"
            FROM "PipelineTemplateVersions" version
            WHERE version."TemplateId" = template."Id"
              AND version."Version" = template."LatestVersion";
            """);

        migrationBuilder.DropTable(name: "PipelineTemplateVersions");
        migrationBuilder.DropColumn(name: "LatestVersion", table: "PipelineTemplates");
        migrationBuilder.DropColumn(name: "OrganizationId", table: "PipelineTemplates");

        migrationBuilder.AlterColumn<string>(
            name: "Name", table: "PipelineTemplates", type: "text", nullable: false,
            oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100);
        migrationBuilder.AlterColumn<string>(
            name: "Description", table: "PipelineTemplates", type: "text", nullable: false,
            oldClrType: typeof(string), oldType: "character varying(500)", oldMaxLength: 500);
        migrationBuilder.AlterColumn<string>(
            name: "Category", table: "PipelineTemplates", type: "text", nullable: false,
            oldClrType: typeof(string), oldType: "character varying(50)", oldMaxLength: 50);
    }
}
