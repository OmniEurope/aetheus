using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddServerTaskContainerFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContainerImage",
            table: "Tasks",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ContainerNetwork",
            table: "Tasks",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ContainerRuntime",
            table: "Tasks",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ContainerImage",
            table: "Tasks");

        migrationBuilder.DropColumn(
            name: "ContainerNetwork",
            table: "Tasks");

        migrationBuilder.DropColumn(
            name: "ContainerRuntime",
            table: "Tasks");
    }
}
