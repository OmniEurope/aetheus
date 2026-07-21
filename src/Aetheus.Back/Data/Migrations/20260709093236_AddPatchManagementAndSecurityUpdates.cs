using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddPatchManagementAndSecurityUpdates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "PatchManagementAvailable",
            table: "Servers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "SecurityUpdatesStates",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Probed = table.Column<bool>(type: "boolean", nullable: false),
                PendingTotal = table.Column<int>(type: "integer", nullable: false),
                PendingSecurity = table.Column<int>(type: "integer", nullable: false),
                UpdatesJson = table.Column<string>(type: "text", nullable: true),
                CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SecurityUpdatesStates", x => x.Id);
                table.ForeignKey(
                    name: "FK_SecurityUpdatesStates_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SecurityUpdatesStates_ServerId",
            table: "SecurityUpdatesStates",
            column: "ServerId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SecurityUpdatesStates");

        migrationBuilder.DropColumn(
            name: "PatchManagementAvailable",
            table: "Servers");
    }
}
