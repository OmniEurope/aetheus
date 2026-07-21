using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddServerHostnameUniqueAndPipelineRowVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Servers_Hostname",
            table: "Servers");

        migrationBuilder.AddColumn<Guid>(
            name: "RowVersion",
            table: "Pipelines",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.CreateIndex(
            name: "IX_Servers_Hostname",
            table: "Servers",
            column: "Hostname",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Servers_Hostname",
            table: "Servers");

        migrationBuilder.DropColumn(
            name: "RowVersion",
            table: "Pipelines");

        migrationBuilder.CreateIndex(
            name: "IX_Servers_Hostname",
            table: "Servers",
            column: "Hostname");
    }
}
