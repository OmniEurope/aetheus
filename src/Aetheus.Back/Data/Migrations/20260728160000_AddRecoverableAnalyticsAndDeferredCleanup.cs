// SPDX-License-Identifier: EUPL-1.2
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

public partial class AddRecoverableAnalyticsAndDeferredCleanup : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "AnalyticsKeyRotationPendingAt",
            table: "MonitoredApps",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AnalyticsPendingPseudonymKeyVersion",
            table: "MonitoredApps",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsDeferredCleanup",
            table: "Tasks",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_IsDeferredCleanup_Status",
            table: "Tasks",
            columns: new[] { "IsDeferredCleanup", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_PipelineRunId_IsDeferredCleanup",
            table: "Tasks",
            columns: new[] { "PipelineRunId", "IsDeferredCleanup" },
            unique: true,
            filter: "\"IsDeferredCleanup\" = TRUE AND \"PipelineRunId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Tasks_IsDeferredCleanup_Status",
            table: "Tasks");

        migrationBuilder.DropIndex(
            name: "IX_Tasks_PipelineRunId_IsDeferredCleanup",
            table: "Tasks");

        migrationBuilder.DropColumn(
            name: "AnalyticsKeyRotationPendingAt",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsPendingPseudonymKeyVersion",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "IsDeferredCleanup",
            table: "Tasks");
    }
}
