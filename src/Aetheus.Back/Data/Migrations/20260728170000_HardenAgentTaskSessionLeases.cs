// SPDX-License-Identifier: EUPL-1.2
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

public partial class HardenAgentTaskSessionLeases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "AgentSessionFencingToken",
            table: "Servers",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<DateTime>(
            name: "AgentSessionLeaseExpiresAt",
            table: "Servers",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "AssignedAgentSessionFencingToken",
            table: "Tasks",
            type: "bigint",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AgentSessionFencingToken",
            table: "Servers");

        migrationBuilder.DropColumn(
            name: "AgentSessionLeaseExpiresAt",
            table: "Servers");

        migrationBuilder.DropColumn(
            name: "AssignedAgentSessionFencingToken",
            table: "Tasks");
    }
}
