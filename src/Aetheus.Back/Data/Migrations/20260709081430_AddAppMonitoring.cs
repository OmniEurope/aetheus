using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddAppMonitoring : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MonitoredApps",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                EnvironmentId = table.Column<int>(type: "integer", nullable: true),
                ServerId = table.Column<int>(type: "integer", nullable: true),
                Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                ProbeUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                ProbeIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                ProbeTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                ExpectedStatusCode = table.Column<int>(type: "integer", nullable: false),
                FailureThreshold = table.Column<int>(type: "integer", nullable: false),
                RecoveryThreshold = table.Column<int>(type: "integer", nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                CurrentStatus = table.Column<int>(type: "integer", nullable: false),
                LastStatusChangeAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastResponseTimeMs = table.Column<int>(type: "integer", nullable: true),
                ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                ConsecutiveSuccesses = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MonitoredApps", x => x.Id);
                table.ForeignKey(
                    name: "FK_MonitoredApps_Environments_EnvironmentId",
                    column: x => x.EnvironmentId,
                    principalTable: "Environments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_MonitoredApps_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MonitoredApps_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "AppHealthHourly",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                HourUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SampleCount = table.Column<int>(type: "integer", nullable: false),
                UpCount = table.Column<int>(type: "integer", nullable: false),
                AvgResponseTimeMs = table.Column<double>(type: "double precision", nullable: false),
                P95ResponseTimeMs = table.Column<double>(type: "double precision", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppHealthHourly", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppHealthHourly_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AppHealthSamples",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsUp = table.Column<bool>(type: "boolean", nullable: false),
                ResponseTimeMs = table.Column<int>(type: "integer", nullable: true),
                StatusCode = table.Column<int>(type: "integer", nullable: true),
                Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppHealthSamples", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppHealthSamples_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AppHealthHourly_MonitoredAppId_HourUtc",
            table: "AppHealthHourly",
            columns: new[] { "MonitoredAppId", "HourUtc" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AppHealthSamples_MonitoredAppId_Timestamp",
            table: "AppHealthSamples",
            columns: new[] { "MonitoredAppId", "Timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_AppHealthSamples_Timestamp",
            table: "AppHealthSamples",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_MonitoredApps_CurrentStatus",
            table: "MonitoredApps",
            column: "CurrentStatus");

        migrationBuilder.CreateIndex(
            name: "IX_MonitoredApps_EnvironmentId",
            table: "MonitoredApps",
            column: "EnvironmentId");

        migrationBuilder.CreateIndex(
            name: "IX_MonitoredApps_ProjectId",
            table: "MonitoredApps",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_MonitoredApps_ServerId",
            table: "MonitoredApps",
            column: "ServerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppHealthHourly");

        migrationBuilder.DropTable(
            name: "AppHealthSamples");

        migrationBuilder.DropTable(
            name: "MonitoredApps");
    }
}
