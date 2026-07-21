using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddProjectServers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProjectServers",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                ServerId = table.Column<int>(type: "integer", nullable: true),
                Type = table.Column<int>(type: "integer", nullable: false),
                DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Host = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Port = table.Column<int>(type: "integer", nullable: true),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectServers", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectServers_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ProjectServers_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectServers_ProjectId",
            table: "ProjectServers",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectServers_ProjectId_ServerId",
            table: "ProjectServers",
            columns: new[] { "ProjectId", "ServerId" },
            unique: true,
            filter: "\"ServerId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectServers_ServerId",
            table: "ProjectServers",
            column: "ServerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProjectServers");
    }
}
