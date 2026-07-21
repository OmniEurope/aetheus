using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddProjectArtifactRetentionAndReleasePattern : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ArtifactLatestRetentionDays",
            table: "Projects",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ArtifactRetentionDays",
            table: "Projects",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReleaseNumberingPattern",
            table: "Projects",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ArtifactLatestRetentionDays",
            table: "Projects");

        migrationBuilder.DropColumn(
            name: "ArtifactRetentionDays",
            table: "Projects");

        migrationBuilder.DropColumn(
            name: "ReleaseNumberingPattern",
            table: "Projects");
    }
}
