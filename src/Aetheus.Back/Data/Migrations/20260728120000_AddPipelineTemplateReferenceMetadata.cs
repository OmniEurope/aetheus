// SPDX-License-Identifier: EUPL-1.2
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

public sealed partial class AddPipelineTemplateReferenceMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TemplateReferenceName",
            table: "Pipelines",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TemplateReferenceVersion",
            table: "Pipelines",
            type: "integer",
            nullable: true);

        // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: backfills only the new nullable metadata column from
        // the existing YAML mirror; no existing column, row, constraint, or historical value is removed.
        migrationBuilder.Sql(
            """
            UPDATE "Pipelines"
            SET "TemplateReferenceName" = trim(both ' ''"' from substring(
                "YamlDefinition" from '(?im)^[[:space:]]*extends[[:space:]]*:[[:space:]]*[''"]?([^#\r\n''"]+)'))
            WHERE "YamlDefinition" ~* '(?m)^[[:space:]]*extends[[:space:]]*:';

            UPDATE "Pipelines"
            SET "TemplateReferenceName" = trim(both ' ''"' from substring(
                    "YamlDefinition" from '(?im)^[[:space:]]*extends[[:space:]]*:[[:space:]]*[''"]?([^@#\r\n''"]+)@[0-9]+')),
                "TemplateReferenceVersion" = substring(
                    "YamlDefinition" from '(?im)^[[:space:]]*extends[[:space:]]*:[^#\r\n]*@([0-9]+)')::integer
            WHERE "YamlDefinition" ~* '(?m)^[[:space:]]*extends[[:space:]]*:[^#\r\n]*@[0-9]+';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Pipelines_TemplateReferenceName_TemplateReferenceVersion",
            table: "Pipelines",
            columns: new[] { "TemplateReferenceName", "TemplateReferenceVersion" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Pipelines_TemplateReferenceName_TemplateReferenceVersion",
            table: "Pipelines");

        migrationBuilder.DropColumn(
            name: "TemplateReferenceName",
            table: "Pipelines");

        migrationBuilder.DropColumn(
            name: "TemplateReferenceVersion",
            table: "Pipelines");
    }
}
