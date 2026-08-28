using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddAgentUpdateRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AgentUpdateReserved",
            table: "Servers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "AgentUpdateReservedAt",
            table: "Servers",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AgentUpdateRequests",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                TargetVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ObservedVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ObservedProtocolVersion = table.Column<int>(type: "integer", nullable: true),
                ObservedSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                TaskId = table.Column<int>(type: "integer", nullable: true),
                ExpectedCapabilitiesJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                HandoffAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ConfirmationDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ConfirmedSessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                FailureDiagnostic = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AgentUpdateRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_AgentUpdateRequests_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AgentUpdateRequests_Tasks_TaskId",
                    column: x => x.TaskId,
                    principalTable: "Tasks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AgentUpdateRequests_IsActive_Status",
            table: "AgentUpdateRequests",
            columns: new[] { "IsActive", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_AgentUpdateRequests_ServerId_TargetVersion",
            table: "AgentUpdateRequests",
            columns: new[] { "ServerId", "TargetVersion" },
            unique: true,
            filter: "\"IsActive\" = TRUE");

        migrationBuilder.CreateIndex(
            name: "IX_AgentUpdateRequests_TaskId",
            table: "AgentUpdateRequests",
            column: "TaskId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AgentUpdateRequests");

        migrationBuilder.DropColumn(
            name: "AgentUpdateReserved",
            table: "Servers");

        migrationBuilder.DropColumn(
            name: "AgentUpdateReservedAt",
            table: "Servers");
    }
}
