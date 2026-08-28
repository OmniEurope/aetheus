using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddDastExecutionLeases : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DastExecutionLeases",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PipelineRunId = table.Column<int>(type: "integer", nullable: false),
                PipelineStepRunId = table.Column<int>(type: "integer", nullable: false),
                EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                TargetHost = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                TargetPort = table.Column<int>(type: "integer", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DastExecutionLeases", x => x.Id);
                table.ForeignKey(
                    name: "FK_DastExecutionLeases_Environments_EnvironmentId",
                    column: x => x.EnvironmentId,
                    principalTable: "Environments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DastExecutionLeases_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DastExecutionLeases_PipelineStepRuns_PipelineStepRunId",
                    column: x => x.PipelineStepRunId,
                    principalTable: "PipelineStepRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DastExecutionLeases_EnvironmentId_TargetHost_TargetPort",
            table: "DastExecutionLeases",
            columns: new[] { "EnvironmentId", "TargetHost", "TargetPort" });

        migrationBuilder.CreateIndex(
            name: "IX_DastExecutionLeases_PipelineRunId_ExpiresAt",
            table: "DastExecutionLeases",
            columns: new[] { "PipelineRunId", "ExpiresAt" });

        migrationBuilder.CreateIndex(
            name: "IX_DastExecutionLeases_PipelineStepRunId",
            table: "DastExecutionLeases",
            column: "PipelineStepRunId");

        migrationBuilder.CreateIndex(
            name: "IX_DastExecutionLeases_Token",
            table: "DastExecutionLeases",
            column: "Token",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DastExecutionLeases");
    }
}
