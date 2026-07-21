using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddServerTaskAssignedAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "AssignedAt",
            table: "Tasks",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_Status_AssignedAt",
            table: "Tasks",
            columns: new[] { "Status", "AssignedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Tasks_Status_AssignedAt",
            table: "Tasks");

        migrationBuilder.DropColumn(
            name: "AssignedAt",
            table: "Tasks");
    }
}
