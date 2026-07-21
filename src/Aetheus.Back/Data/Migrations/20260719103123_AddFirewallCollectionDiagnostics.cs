using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddFirewallCollectionDiagnostics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CollectionDiagnostics",
            table: "FirewallStates",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<bool>(
            name: "StatusKnown",
            table: "FirewallStates",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CollectionDiagnostics",
            table: "FirewallStates");

        migrationBuilder.DropColumn(
            name: "StatusKnown",
            table: "FirewallStates");
    }
}
