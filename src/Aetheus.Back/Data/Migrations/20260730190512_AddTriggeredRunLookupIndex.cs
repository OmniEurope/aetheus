using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddTriggeredRunLookupIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_PipelineStepRuns_TriggeredRunId",
            table: "PipelineStepRuns",
            column: "TriggeredRunId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PipelineStepRuns_TriggeredRunId",
            table: "PipelineStepRuns");
    }
}
