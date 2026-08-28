using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddAgentProtocolCompatibility : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AgentCapabilitiesJson",
            table: "Servers",
            type: "character varying(8192)",
            maxLength: 8192,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AgentProtocolVersion",
            table: "Servers",
            type: "integer",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AgentCapabilitiesJson",
            table: "Servers");

        migrationBuilder.DropColumn(
            name: "AgentProtocolVersion",
            table: "Servers");
    }
}
