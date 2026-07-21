using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class BindPipelineRunArtifactsNav : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_PipelineArtifacts_PipelineRuns_PipelineRunId1",
            table: "PipelineArtifacts");

        migrationBuilder.DropIndex(
            name: "IX_PipelineArtifacts_PipelineRunId1",
            table: "PipelineArtifacts");

        migrationBuilder.DropColumn(
            name: "PipelineRunId1",
            table: "PipelineArtifacts");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "PipelineRunId1",
            table: "PipelineArtifacts",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PipelineArtifacts_PipelineRunId1",
            table: "PipelineArtifacts",
            column: "PipelineRunId1");

        migrationBuilder.AddForeignKey(
            name: "FK_PipelineArtifacts_PipelineRuns_PipelineRunId1",
            table: "PipelineArtifacts",
            column: "PipelineRunId1",
            principalTable: "PipelineRuns",
            principalColumn: "Id");
    }
}
