using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddOrganizationOwnership : Migration
{
    // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: deterministic seed/backfill before adding ownership constraints.
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Projects_Organizations_OrganizationId",
            table: "Projects");

        migrationBuilder.AddColumn<int>(
            name: "OrganizationId",
            table: "Servers",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "OrganizationId",
            table: "RegistrationTokens",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AlterColumn<int>(
            name: "OrganizationId",
            table: "Projects",
            type: "integer",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);

        migrationBuilder.AddColumn<int>(
            name: "OrganizationId",
            table: "PluginRegistrations",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_Servers_OrganizationId",
            table: "Servers",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_RegistrationTokens_OrganizationId",
            table: "RegistrationTokens",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_PluginRegistrations_OrganizationId",
            table: "PluginRegistrations",
            column: "OrganizationId");

        // Existing installs already have Projects/Servers/RegistrationTokens/PluginRegistrations
        // rows. The AddColumn/AlterColumn calls above default their new OrganizationId to the
        // sentinel 0, which references no Organization (identity ids start at 1, and the default
        // org is only created by DbInitializer - which early-returns on a populated DB and runs
        // *after* migrations). The FK constraints below would therefore fail with 23503 on every
        // non-empty database. Seed the default "Aetheus" organization here (idempotent: slug
        // is unique and DbInitializer.EnsureDefaultOrganizationAsync reuses it) and backfill the
        // sentinel rows so every FK is satisfiable at constraint-creation time.
        migrationBuilder.Sql("""
            INSERT INTO "Organizations" ("Name", "Slug", "Description", "CreatedAt", "UpdatedAt")
            SELECT 'Aetheus', 'aetheus',
                   'Default organization seeded on first run; owns all bootstrap resources.',
                   now(), now()
            WHERE NOT EXISTS (SELECT 1 FROM "Organizations" WHERE "Slug" = 'aetheus');

            UPDATE "Projects" SET "OrganizationId" = o."Id"
            FROM "Organizations" o WHERE o."Slug" = 'aetheus' AND "Projects"."OrganizationId" = 0;

            UPDATE "Servers" SET "OrganizationId" = o."Id"
            FROM "Organizations" o WHERE o."Slug" = 'aetheus' AND "Servers"."OrganizationId" = 0;

            UPDATE "RegistrationTokens" SET "OrganizationId" = o."Id"
            FROM "Organizations" o WHERE o."Slug" = 'aetheus' AND "RegistrationTokens"."OrganizationId" = 0;

            UPDATE "PluginRegistrations" SET "OrganizationId" = o."Id"
            FROM "Organizations" o WHERE o."Slug" = 'aetheus' AND "PluginRegistrations"."OrganizationId" = 0;
            """);

        migrationBuilder.AddForeignKey(
            name: "FK_PluginRegistrations_Organizations_OrganizationId",
            table: "PluginRegistrations",
            column: "OrganizationId",
            principalTable: "Organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Projects_Organizations_OrganizationId",
            table: "Projects",
            column: "OrganizationId",
            principalTable: "Organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_RegistrationTokens_Organizations_OrganizationId",
            table: "RegistrationTokens",
            column: "OrganizationId",
            principalTable: "Organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Servers_Organizations_OrganizationId",
            table: "Servers",
            column: "OrganizationId",
            principalTable: "Organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_PluginRegistrations_Organizations_OrganizationId",
            table: "PluginRegistrations");

        migrationBuilder.DropForeignKey(
            name: "FK_Projects_Organizations_OrganizationId",
            table: "Projects");

        migrationBuilder.DropForeignKey(
            name: "FK_RegistrationTokens_Organizations_OrganizationId",
            table: "RegistrationTokens");

        migrationBuilder.DropForeignKey(
            name: "FK_Servers_Organizations_OrganizationId",
            table: "Servers");

        migrationBuilder.DropIndex(
            name: "IX_Servers_OrganizationId",
            table: "Servers");

        migrationBuilder.DropIndex(
            name: "IX_RegistrationTokens_OrganizationId",
            table: "RegistrationTokens");

        migrationBuilder.DropIndex(
            name: "IX_PluginRegistrations_OrganizationId",
            table: "PluginRegistrations");

        migrationBuilder.DropColumn(
            name: "OrganizationId",
            table: "Servers");

        migrationBuilder.DropColumn(
            name: "OrganizationId",
            table: "RegistrationTokens");

        migrationBuilder.DropColumn(
            name: "OrganizationId",
            table: "PluginRegistrations");

        migrationBuilder.AlterColumn<int>(
            name: "OrganizationId",
            table: "Projects",
            type: "integer",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "integer");

        migrationBuilder.AddForeignKey(
            name: "FK_Projects_Organizations_OrganizationId",
            table: "Projects",
            column: "OrganizationId",
            principalTable: "Organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }
}
