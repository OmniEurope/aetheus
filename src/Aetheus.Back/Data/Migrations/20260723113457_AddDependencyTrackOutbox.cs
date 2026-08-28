using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddDependencyTrackOutbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DependencyTrackOutboxItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                AnalysisReportId = table.Column<int>(type: "integer", nullable: false),
                PipelineArtifactId = table.Column<int>(type: "integer", nullable: true),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                ExternalProjectName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ProjectVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ReportEntryPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DependencyTrackOutboxItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_DependencyTrackOutboxItems_AnalysisReports_AnalysisReportId",
                    column: x => x.AnalysisReportId,
                    principalTable: "AnalysisReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DependencyTrackOutboxItems_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DependencyTrackOutboxItems_PipelineArtifacts_PipelineArtifa~",
                    column: x => x.PipelineArtifactId,
                    principalTable: "PipelineArtifacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DependencyTrackOutboxItems_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DependencyTrackOutboxItems_AnalysisReportId",
            table: "DependencyTrackOutboxItems",
            column: "AnalysisReportId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DependencyTrackOutboxItems_OrganizationId",
            table: "DependencyTrackOutboxItems",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_DependencyTrackOutboxItems_PipelineArtifactId",
            table: "DependencyTrackOutboxItems",
            column: "PipelineArtifactId");

        migrationBuilder.CreateIndex(
            name: "IX_DependencyTrackOutboxItems_ProjectId_CreatedAt",
            table: "DependencyTrackOutboxItems",
            columns: new[] { "ProjectId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_DependencyTrackOutboxItems_Status_NextAttemptAt",
            table: "DependencyTrackOutboxItems",
            columns: new[] { "Status", "NextAttemptAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DependencyTrackOutboxItems");
    }
}
