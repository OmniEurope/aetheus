using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddReleaseCommitTagAndStepGroupName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CommitHash",
            table: "Releases",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TagName",
            table: "Releases",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GroupName",
            table: "PipelineStepRuns",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CommitHash",
            table: "Releases");

        migrationBuilder.DropColumn(
            name: "TagName",
            table: "Releases");

        migrationBuilder.DropColumn(
            name: "GroupName",
            table: "PipelineStepRuns");
    }
}
