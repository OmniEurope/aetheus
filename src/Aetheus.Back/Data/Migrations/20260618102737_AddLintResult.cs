using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddLintResult : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LintResults",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PipelineRunId = table.Column<int>(type: "integer", nullable: false),
                StageName = table.Column<string>(type: "text", nullable: true),
                StepName = table.Column<string>(type: "text", nullable: true),
                Tool = table.Column<string>(type: "text", nullable: true),
                ErrorCount = table.Column<int>(type: "integer", nullable: false),
                WarningCount = table.Column<int>(type: "integer", nullable: false),
                InfoCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LintResults", x => x.Id);
                table.ForeignKey(
                    name: "FK_LintResults_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LintResults_PipelineRunId",
            table: "LintResults",
            column: "PipelineRunId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "LintResults");
    }
}
