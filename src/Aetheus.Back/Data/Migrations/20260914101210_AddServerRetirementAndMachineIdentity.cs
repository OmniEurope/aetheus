using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServerRetirementAndMachineIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Servers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MachineIdHash",
                table: "Servers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Servers_OrganizationId_MachineIdHash",
                table: "Servers",
                columns: new[] { "OrganizationId", "MachineIdHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Servers_OrganizationId_MachineIdHash",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "MachineIdHash",
                table: "Servers");
        }
    }
}
