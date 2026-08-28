using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddVerifiedCheckpointAndAnalysisProducerContracts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AssignedAgentVersion",
            table: "Tasks",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AssignedScannerManifestSha256",
            table: "Tasks",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "RetentionLeaseExpiresAt",
            table: "PipelineArtifacts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PayloadHash",
            table: "AnalysisReports",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisReports_PipelineRunId_ScannerKey_StageName_StepName~",
            table: "AnalysisReports",
            columns: new[] { "PipelineRunId", "ScannerKey", "StageName", "StepName", "PayloadHash" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AnalysisReports_PipelineRunId_ScannerKey_StageName_StepName~",
            table: "AnalysisReports");

        migrationBuilder.DropColumn(
            name: "AssignedAgentVersion",
            table: "Tasks");

        migrationBuilder.DropColumn(
            name: "AssignedScannerManifestSha256",
            table: "Tasks");

        migrationBuilder.DropColumn(
            name: "RetentionLeaseExpiresAt",
            table: "PipelineArtifacts");

        migrationBuilder.DropColumn(
            name: "PayloadHash",
            table: "AnalysisReports");
    }
}
