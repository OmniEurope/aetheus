using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddAppTelemetry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "IngestDroppedCount",
            table: "MonitoredApps",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<DateTime>(
            name: "IngestKeyCreatedAt",
            table: "MonitoredApps",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "IngestKeyHash",
            table: "MonitoredApps",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "LastIngestAt",
            table: "MonitoredApps",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AppErrorEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ExceptionType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                TopFrame = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppErrorEvents", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppErrorEvents_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AppLogEntries",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SeverityNumber = table.Column<int>(type: "integer", nullable: false),
                SeverityText = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                AttributesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppLogEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppLogEntries_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AppMetricHourly",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                MetricName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                HourUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SampleCount = table.Column<int>(type: "integer", nullable: false),
                MinValue = table.Column<double>(type: "double precision", nullable: false),
                MaxValue = table.Column<double>(type: "double precision", nullable: false),
                AvgValue = table.Column<double>(type: "double precision", nullable: false),
                P95Value = table.Column<double>(type: "double precision", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppMetricHourly", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppMetricHourly_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AppMetricSamples",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                MetricName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Value = table.Column<double>(type: "double precision", nullable: false),
                Unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                AttributesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppMetricSamples", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppMetricSamples_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AppMetricThresholds",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                MetricName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Operator = table.Column<int>(type: "integer", nullable: false),
                Threshold = table.Column<double>(type: "double precision", nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                IsBreached = table.Column<bool>(type: "boolean", nullable: false),
                LastTriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppMetricThresholds", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppMetricThresholds_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MonitoredApps_IngestKeyHash",
            table: "MonitoredApps",
            column: "IngestKeyHash",
            unique: true,
            filter: "\"IngestKeyHash\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_AppErrorEvents_MonitoredAppId_Fingerprint",
            table: "AppErrorEvents",
            columns: new[] { "MonitoredAppId", "Fingerprint" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AppErrorEvents_MonitoredAppId_LastSeenAt",
            table: "AppErrorEvents",
            columns: new[] { "MonitoredAppId", "LastSeenAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AppLogEntries_MonitoredAppId_Timestamp",
            table: "AppLogEntries",
            columns: new[] { "MonitoredAppId", "Timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_AppLogEntries_Timestamp",
            table: "AppLogEntries",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_AppMetricHourly_MonitoredAppId_MetricName_HourUtc",
            table: "AppMetricHourly",
            columns: new[] { "MonitoredAppId", "MetricName", "HourUtc" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AppMetricSamples_MonitoredAppId_MetricName_Timestamp",
            table: "AppMetricSamples",
            columns: new[] { "MonitoredAppId", "MetricName", "Timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_AppMetricSamples_Timestamp",
            table: "AppMetricSamples",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_AppMetricThresholds_MonitoredAppId_MetricName",
            table: "AppMetricThresholds",
            columns: new[] { "MonitoredAppId", "MetricName" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppErrorEvents");

        migrationBuilder.DropTable(
            name: "AppLogEntries");

        migrationBuilder.DropTable(
            name: "AppMetricHourly");

        migrationBuilder.DropTable(
            name: "AppMetricSamples");

        migrationBuilder.DropTable(
            name: "AppMetricThresholds");

        migrationBuilder.DropIndex(
            name: "IX_MonitoredApps_IngestKeyHash",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "IngestDroppedCount",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "IngestKeyCreatedAt",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "IngestKeyHash",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "LastIngestAt",
            table: "MonitoredApps");
    }
}
