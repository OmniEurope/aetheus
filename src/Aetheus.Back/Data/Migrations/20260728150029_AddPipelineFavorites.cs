using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddPipelineFavorites : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PipelineFavorites",
            columns: table => new
            {
                UserId = table.Column<int>(type: "integer", nullable: false),
                PipelineId = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PipelineFavorites", x => new { x.UserId, x.PipelineId });
                table.ForeignKey(
                    name: "FK_PipelineFavorites_Pipelines_PipelineId",
                    column: x => x.PipelineId,
                    principalTable: "Pipelines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PipelineFavorites_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PipelineFavorites_PipelineId",
            table: "PipelineFavorites",
            column: "PipelineId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PipelineFavorites");
    }
}
