using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddArtifactRetentionAndReleaseChangelog : Migration
{
    // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: data-only backfill for newly added nullable/defaulted columns.
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "BuildNumber",
            table: "Releases",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "Changelog",
            table: "Releases",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EnvironmentName",
            table: "PipelineArtifacts",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PipelineId",
            table: "PipelineArtifacts",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "ProjectId",
            table: "PipelineArtifacts",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ReleaseId",
            table: "PipelineArtifacts",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "RetentionExpiresAt",
            table: "PipelineArtifacts",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

        migrationBuilder.AddColumn<int>(
            name: "RetentionPolicy",
            table: "PipelineArtifacts",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        // Backfill PipelineId from PipelineRun for existing artifacts
        migrationBuilder.Sql("""
            UPDATE "PipelineArtifacts"
            SET "PipelineId" = pr."PipelineId"
            FROM "PipelineRuns" pr
            WHERE "PipelineArtifacts"."PipelineRunId" = pr."Id"
              AND "PipelineArtifacts"."PipelineId" = 0
            """);

        // Backfill RetentionExpiresAt so pre-existing artifacts are not immediately expired
        migrationBuilder.Sql("""
            UPDATE "PipelineArtifacts"
            SET "RetentionExpiresAt" = NOW() + INTERVAL '365 days'
            WHERE "RetentionExpiresAt" = '0001-01-01T00:00:00Z'
            """);

        migrationBuilder.CreateIndex(
            name: "IX_PipelineArtifacts_PipelineId",
            table: "PipelineArtifacts",
            column: "PipelineId");

        migrationBuilder.CreateIndex(
            name: "IX_PipelineArtifacts_ProjectId_PipelineId_RetentionPolicy",
            table: "PipelineArtifacts",
            columns: new[] { "ProjectId", "PipelineId", "RetentionPolicy" });

        migrationBuilder.CreateIndex(
            name: "IX_PipelineArtifacts_ReleaseId",
            table: "PipelineArtifacts",
            column: "ReleaseId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PipelineArtifacts_RetentionExpiresAt",
            table: "PipelineArtifacts",
            column: "RetentionExpiresAt");

        migrationBuilder.AddForeignKey(
            name: "FK_PipelineArtifacts_Pipelines_PipelineId",
            table: "PipelineArtifacts",
            column: "PipelineId",
            principalTable: "Pipelines",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_PipelineArtifacts_Projects_ProjectId",
            table: "PipelineArtifacts",
            column: "ProjectId",
            principalTable: "Projects",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_PipelineArtifacts_Releases_ReleaseId",
            table: "PipelineArtifacts",
            column: "ReleaseId",
            principalTable: "Releases",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_PipelineArtifacts_Pipelines_PipelineId",
            table: "PipelineArtifacts");

        migrationBuilder.DropForeignKey(
            name: "FK_PipelineArtifacts_Projects_ProjectId",
            table: "PipelineArtifacts");

        migrationBuilder.DropForeignKey(
            name: "FK_PipelineArtifacts_Releases_ReleaseId",
            table: "PipelineArtifacts");

        migrationBuilder.DropIndex(
            name: "IX_PipelineArtifacts_PipelineId",
            table: "PipelineArtifacts");

        migrationBuilder.DropIndex(
            name: "IX_PipelineArtifacts_ProjectId_PipelineId_RetentionPolicy",
            table: "PipelineArtifacts");

        migrationBuilder.DropIndex(
            name: "IX_PipelineArtifacts_ReleaseId",
            table: "PipelineArtifacts");

        migrationBuilder.DropIndex(
            name: "IX_PipelineArtifacts_RetentionExpiresAt",
            table: "PipelineArtifacts");

        migrationBuilder.DropColumn(
            name: "BuildNumber",
            table: "Releases");

        migrationBuilder.DropColumn(
            name: "Changelog",
            table: "Releases");

        migrationBuilder.DropColumn(
            name: "EnvironmentName",
            table: "PipelineArtifacts");

        migrationBuilder.DropColumn(
            name: "PipelineId",
            table: "PipelineArtifacts");

        migrationBuilder.DropColumn(
            name: "ProjectId",
            table: "PipelineArtifacts");

        migrationBuilder.DropColumn(
            name: "ReleaseId",
            table: "PipelineArtifacts");

        migrationBuilder.DropColumn(
            name: "RetentionExpiresAt",
            table: "PipelineArtifacts");

        migrationBuilder.DropColumn(
            name: "RetentionPolicy",
            table: "PipelineArtifacts");
    }
}
