using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddReleaseRollbacks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReleaseRollbacks",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SourceReleaseId = table.Column<int>(type: "integer", nullable: false),
                TargetReleaseId = table.Column<int>(type: "integer", nullable: false),
                PipelineId = table.Column<int>(type: "integer", nullable: false),
                PipelineRunId = table.Column<int>(type: "integer", nullable: true),
                BackupRunId = table.Column<int>(type: "integer", nullable: true),
                RestoreDatabase = table.Column<bool>(type: "boolean", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReleaseRollbacks", x => x.Id);
                table.ForeignKey(
                    name: "FK_ReleaseRollbacks_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_ReleaseRollbacks_Pipelines_PipelineId",
                    column: x => x.PipelineId,
                    principalTable: "Pipelines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ReleaseRollbacks_Releases_SourceReleaseId",
                    column: x => x.SourceReleaseId,
                    principalTable: "Releases",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ReleaseRollbacks_Releases_TargetReleaseId",
                    column: x => x.TargetReleaseId,
                    principalTable: "Releases",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ReleaseRollbacks_PipelineId",
            table: "ReleaseRollbacks",
            column: "PipelineId");

        migrationBuilder.CreateIndex(
            name: "IX_ReleaseRollbacks_PipelineRunId",
            table: "ReleaseRollbacks",
            column: "PipelineRunId",
            unique: true,
            filter: "\"PipelineRunId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_ReleaseRollbacks_SourceReleaseId_RequestedAt",
            table: "ReleaseRollbacks",
            columns: new[] { "SourceReleaseId", "RequestedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ReleaseRollbacks_TargetReleaseId",
            table: "ReleaseRollbacks",
            column: "TargetReleaseId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ReleaseRollbacks");
    }
}
