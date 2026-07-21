using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddPipelineStepRunCompositeIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_PipelineStepRuns_PipelineRunId_StageName",
            table: "PipelineStepRuns",
            columns: new[] { "PipelineRunId", "StageName" });

        migrationBuilder.CreateIndex(
            name: "IX_PipelineStepRuns_PipelineRunId_Status",
            table: "PipelineStepRuns",
            columns: new[] { "PipelineRunId", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PipelineStepRuns_PipelineRunId_StageName",
            table: "PipelineStepRuns");

        migrationBuilder.DropIndex(
            name: "IX_PipelineStepRuns_PipelineRunId_Status",
            table: "PipelineStepRuns");
    }
}
