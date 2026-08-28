using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddAnalysisGrades : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Grade",
            table: "AnalysisEvaluations",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "GradeCompleteness",
            table: "AnalysisEvaluations",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "GradeSnapshotHash",
            table: "AnalysisEvaluations",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "GradeSnapshotJson",
            table: "AnalysisEvaluations",
            type: "character varying(100000)",
            maxLength: 100000,
            nullable: false,
            defaultValue: "");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Grade",
            table: "AnalysisEvaluations");

        migrationBuilder.DropColumn(
            name: "GradeCompleteness",
            table: "AnalysisEvaluations");

        migrationBuilder.DropColumn(
            name: "GradeSnapshotHash",
            table: "AnalysisEvaluations");

        migrationBuilder.DropColumn(
            name: "GradeSnapshotJson",
            table: "AnalysisEvaluations");
    }
}
