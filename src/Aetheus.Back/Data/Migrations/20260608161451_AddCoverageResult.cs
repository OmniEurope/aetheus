using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddCoverageResult : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CoverageResults",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PipelineRunId = table.Column<int>(type: "integer", nullable: false),
                StageName = table.Column<string>(type: "text", nullable: true),
                StepName = table.Column<string>(type: "text", nullable: true),
                LineRate = table.Column<double>(type: "double precision", nullable: false),
                BranchRate = table.Column<double>(type: "double precision", nullable: false),
                LinesCovered = table.Column<int>(type: "integer", nullable: false),
                LinesValid = table.Column<int>(type: "integer", nullable: false),
                BranchesCovered = table.Column<int>(type: "integer", nullable: false),
                BranchesValid = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CoverageResults", x => x.Id);
                table.ForeignKey(
                    name: "FK_CoverageResults_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CoverageResults_PipelineRunId",
            table: "CoverageResults",
            column: "PipelineRunId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CoverageResults");
    }
}
