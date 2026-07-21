using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddStorageDiagnosticAvailability : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "BuildCacheAvailable",
            table: "ServerMetrics",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "DockerInventoryAvailable",
            table: "ServerMetrics",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BuildCacheAvailable",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "DockerInventoryAvailable",
            table: "ServerMetrics");
    }
}
