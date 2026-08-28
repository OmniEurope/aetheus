// SPDX-License-Identifier: EUPL-1.2
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

public sealed partial class AddAiTasks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiRunnerProfiles",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Binary = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                ArgsTemplateJson = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                EnvironmentJsonEncrypted = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: false),
                TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                MaxOutputBytes = table.Column<int>(type: "integer", nullable: false),
                SendsDataExternally = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiRunnerProfiles", x => x.Id);
                table.ForeignKey(
                    name: "FK_AiRunnerProfiles_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AiTaskDefinitions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                ProfileId = table.Column<int>(type: "integer", nullable: false),
                PromptTemplate = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                ServerId = table.Column<int>(type: "integer", nullable: true),
                Schedule = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                LastScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiTaskDefinitions", x => x.Id);
                table.CheckConstraint(
                    "CK_AiTaskDefinitions_ExactlyOneOwner",
                    "(\"ProjectId\" IS NOT NULL AND \"ServerId\" IS NULL) OR (\"ProjectId\" IS NULL AND \"ServerId\" IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_AiTaskDefinitions_AiRunnerProfiles_ProfileId",
                    column: x => x.ProfileId,
                    principalTable: "AiRunnerProfiles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AiTaskDefinitions_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AiTaskDefinitions_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AiRunResults",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerTaskId = table.Column<int>(type: "integer", nullable: false),
                PipelineRunId = table.Column<int>(type: "integer", nullable: true),
                AiTaskDefinitionId = table.Column<int>(type: "integer", nullable: true),
                ProfileName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                SendsDataExternally = table.Column<bool>(type: "boolean", nullable: false),
                ReportMarkdown = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: false),
                Verdict = table.Column<int>(type: "integer", nullable: false),
                DiffPatch = table.Column<string>(type: "character varying(1000000)", maxLength: 1000000, nullable: true),
                DurationMs = table.Column<long>(type: "bigint", nullable: false),
                Truncated = table.Column<bool>(type: "boolean", nullable: false),
                Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                ProposedRepositoryId = table.Column<int>(type: "integer", nullable: true),
                ProposedBranchName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ProposedCommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiRunResults", x => x.Id);
                table.ForeignKey(
                    name: "FK_AiRunResults_AiTaskDefinitions_AiTaskDefinitionId",
                    column: x => x.AiTaskDefinitionId,
                    principalTable: "AiTaskDefinitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AiRunResults_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AiRunResults_Tasks_ServerTaskId",
                    column: x => x.ServerTaskId,
                    principalTable: "Tasks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AiTaskTriggers",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                AiTaskDefinitionId = table.Column<int>(type: "integer", nullable: false),
                EventType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                LastTriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AiTaskTriggers", x => x.Id);
                table.ForeignKey(
                    name: "FK_AiTaskTriggers_AiTaskDefinitions_AiTaskDefinitionId",
                    column: x => x.AiTaskDefinitionId,
                    principalTable: "AiTaskDefinitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AiRunnerProfiles_OrganizationId_Name",
            table: "AiRunnerProfiles",
            columns: new[] { "OrganizationId", "Name" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_AiRunResults_AiTaskDefinitionId_CreatedAt",
            table: "AiRunResults",
            columns: new[] { "AiTaskDefinitionId", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_AiRunResults_PipelineRunId_CreatedAt",
            table: "AiRunResults",
            columns: new[] { "PipelineRunId", "CreatedAt" });
        migrationBuilder.CreateIndex(
            name: "IX_AiRunResults_ServerTaskId",
            table: "AiRunResults",
            column: "ServerTaskId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_AiTaskDefinitions_Enabled_Schedule",
            table: "AiTaskDefinitions",
            columns: new[] { "Enabled", "Schedule" });
        migrationBuilder.CreateIndex(
            name: "IX_AiTaskDefinitions_ProfileId",
            table: "AiTaskDefinitions",
            column: "ProfileId");
        migrationBuilder.CreateIndex(
            name: "IX_AiTaskDefinitions_ProjectId",
            table: "AiTaskDefinitions",
            column: "ProjectId");
        migrationBuilder.CreateIndex(
            name: "IX_AiTaskDefinitions_ServerId",
            table: "AiTaskDefinitions",
            column: "ServerId");
        migrationBuilder.CreateIndex(
            name: "IX_AiTaskTriggers_AiTaskDefinitionId_EventType",
            table: "AiTaskTriggers",
            columns: new[] { "AiTaskDefinitionId", "EventType" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_AiTaskTriggers_EventType",
            table: "AiTaskTriggers",
            column: "EventType");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AiRunResults");
        migrationBuilder.DropTable(name: "AiTaskTriggers");
        migrationBuilder.DropTable(name: "AiTaskDefinitions");
        migrationBuilder.DropTable(name: "AiRunnerProfiles");
    }
}
