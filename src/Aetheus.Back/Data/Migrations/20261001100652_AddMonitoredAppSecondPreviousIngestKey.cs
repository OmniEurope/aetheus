using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoredAppSecondPreviousIngestKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecondPreviousIngestKeyHash",
                table: "MonitoredApps",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SecondPreviousIngestKeyValidUntil",
                table: "MonitoredApps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredApps_SecondPreviousIngestKeyHash",
                table: "MonitoredApps",
                column: "SecondPreviousIngestKeyHash",
                unique: true,
                filter: "\"SecondPreviousIngestKeyHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonitoredApps_SecondPreviousIngestKeyHash",
                table: "MonitoredApps");

            migrationBuilder.DropColumn(
                name: "SecondPreviousIngestKeyHash",
                table: "MonitoredApps");

            migrationBuilder.DropColumn(
                name: "SecondPreviousIngestKeyValidUntil",
                table: "MonitoredApps");
        }
    }
}
