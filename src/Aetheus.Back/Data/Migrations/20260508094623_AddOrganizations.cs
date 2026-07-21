using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddOrganizations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "OrganizationId",
            table: "Projects",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Organizations",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Organizations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "OrganizationMembers",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                UserId = table.Column<int>(type: "integer", nullable: false),
                Role = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrganizationMembers", x => x.Id);
                table.ForeignKey(
                    name: "FK_OrganizationMembers_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_OrganizationMembers_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Projects_OrganizationId",
            table: "Projects",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_OrganizationMembers_OrganizationId_UserId",
            table: "OrganizationMembers",
            columns: new[] { "OrganizationId", "UserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_OrganizationMembers_UserId",
            table: "OrganizationMembers",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_Organizations_Name",
            table: "Organizations",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Organizations_Slug",
            table: "Organizations",
            column: "Slug",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Projects_Organizations_OrganizationId",
            table: "Projects",
            column: "OrganizationId",
            principalTable: "Organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Projects_Organizations_OrganizationId",
            table: "Projects");

        migrationBuilder.DropTable(
            name: "OrganizationMembers");

        migrationBuilder.DropTable(
            name: "Organizations");

        migrationBuilder.DropIndex(
            name: "IX_Projects_OrganizationId",
            table: "Projects");

        migrationBuilder.DropColumn(
            name: "OrganizationId",
            table: "Projects");
    }
}
