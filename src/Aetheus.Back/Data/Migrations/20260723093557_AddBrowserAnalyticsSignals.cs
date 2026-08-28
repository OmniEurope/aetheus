using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddBrowserAnalyticsSignals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "IngestKeyExpiresAt",
            table: "MonitoredApps",
            type: "timestamp with time zone",
            nullable: true,
            defaultValueSql: "CURRENT_TIMESTAMP + INTERVAL '90 days'");

        migrationBuilder.AddColumn<int>(
            name: "DurationMs",
            table: "AppAnalyticsEvents",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ErrorType",
            table: "AppAnalyticsEvents",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IngestKeyExpiresAt",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "DurationMs",
            table: "AppAnalyticsEvents");

        migrationBuilder.DropColumn(
            name: "ErrorType",
            table: "AppAnalyticsEvents");
    }
}
