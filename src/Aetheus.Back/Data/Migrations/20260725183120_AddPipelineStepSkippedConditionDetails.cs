using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddPipelineStepSkippedConditionDetails : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SkippedCondition",
            table: "PipelineStepRuns",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SkippedConditionVariablesJson",
            table: "PipelineStepRuns",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SkippedCondition",
            table: "PipelineStepRuns");

        migrationBuilder.DropColumn(
            name: "SkippedConditionVariablesJson",
            table: "PipelineStepRuns");
    }
}
