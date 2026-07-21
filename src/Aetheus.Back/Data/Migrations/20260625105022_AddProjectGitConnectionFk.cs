using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddProjectGitConnectionFk : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Projects_GitConnectionId",
            table: "Projects",
            column: "GitConnectionId");

        migrationBuilder.AddForeignKey(
            name: "FK_Projects_GitConnections_GitConnectionId",
            table: "Projects",
            column: "GitConnectionId",
            principalTable: "GitConnections",
            principalColumn: "Id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Projects_GitConnections_GitConnectionId",
            table: "Projects");

        migrationBuilder.DropIndex(
            name: "IX_Projects_GitConnectionId",
            table: "Projects");
    }
}
