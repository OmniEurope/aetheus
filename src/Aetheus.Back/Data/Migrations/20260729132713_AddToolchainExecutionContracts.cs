using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddToolchainExecutionContracts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContainerShell",
            table: "Tasks",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ContainerToolchain",
            table: "Tasks",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FailureCode",
            table: "Tasks",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FailureReason",
            table: "Tasks",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ContainerShell",
            table: "Tasks");

        migrationBuilder.DropColumn(
            name: "ContainerToolchain",
            table: "Tasks");

        migrationBuilder.DropColumn(
            name: "FailureCode",
            table: "Tasks");

        migrationBuilder.DropColumn(
            name: "FailureReason",
            table: "Tasks");
    }
}
