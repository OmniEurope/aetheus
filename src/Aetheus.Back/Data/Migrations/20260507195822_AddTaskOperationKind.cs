using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddTaskOperationKind : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TaskLogs_TaskId",
            table: "TaskLogs");

        migrationBuilder.AddColumn<int>(
            name: "Operation",
            table: "Tasks",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_TaskLogs_TaskId_Timestamp",
            table: "TaskLogs",
            columns: new[] { "TaskId", "Timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_TaskLogs_Timestamp",
            table: "TaskLogs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_Servers_Status",
            table: "Servers",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_Servers_Status_LastHeartbeat",
            table: "Servers",
            columns: new[] { "Status", "LastHeartbeat" });

        migrationBuilder.CreateIndex(
            name: "IX_ServerMetrics_Timestamp",
            table: "ServerMetrics",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineRuns_PipelineId_Status",
            table: "PipelineRuns",
            columns: new[] { "PipelineId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_PipelineRuns_Status",
            table: "PipelineRuns",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_Action",
            table: "AuditLogs",
            column: "Action");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_EntityType",
            table: "AuditLogs",
            column: "EntityType");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_EntityType_Action_Timestamp",
            table: "AuditLogs",
            columns: new[] { "EntityType", "Action", "Timestamp" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TaskLogs_TaskId_Timestamp",
            table: "TaskLogs");

        migrationBuilder.DropIndex(
            name: "IX_TaskLogs_Timestamp",
            table: "TaskLogs");

        migrationBuilder.DropIndex(
            name: "IX_Servers_Status",
            table: "Servers");

        migrationBuilder.DropIndex(
            name: "IX_Servers_Status_LastHeartbeat",
            table: "Servers");

        migrationBuilder.DropIndex(
            name: "IX_ServerMetrics_Timestamp",
            table: "ServerMetrics");

        migrationBuilder.DropIndex(
            name: "IX_PipelineRuns_PipelineId_Status",
            table: "PipelineRuns");

        migrationBuilder.DropIndex(
            name: "IX_PipelineRuns_Status",
            table: "PipelineRuns");

        migrationBuilder.DropIndex(
            name: "IX_AuditLogs_Action",
            table: "AuditLogs");

        migrationBuilder.DropIndex(
            name: "IX_AuditLogs_EntityType",
            table: "AuditLogs");

        migrationBuilder.DropIndex(
            name: "IX_AuditLogs_EntityType_Action_Timestamp",
            table: "AuditLogs");

        migrationBuilder.DropColumn(
            name: "Operation",
            table: "Tasks");

        migrationBuilder.CreateIndex(
            name: "IX_TaskLogs_TaskId",
            table: "TaskLogs",
            column: "TaskId");
    }
}
