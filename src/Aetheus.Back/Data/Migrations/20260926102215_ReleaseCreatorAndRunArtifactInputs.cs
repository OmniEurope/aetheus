using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseCreatorAndRunArtifactInputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreatedByPipelineRunId",
                table: "Releases",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PipelineRunArtifactInputs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PipelineRunId = table.Column<int>(type: "integer", nullable: false),
                    StepName = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ArtifactId = table.Column<int>(type: "integer", nullable: true),
                    ArtifactName = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourcePipelineRunId = table.Column<int>(type: "integer", nullable: false),
                    ReleaseId = table.Column<int>(type: "integer", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineRunArtifactInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineRunArtifactInputs_PipelineArtifacts_ArtifactId",
                        column: x => x.ArtifactId,
                        principalTable: "PipelineArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PipelineRunArtifactInputs_PipelineRuns_PipelineRunId",
                        column: x => x.PipelineRunId,
                        principalTable: "PipelineRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PipelineRunArtifactInputs_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Releases_CreatedByPipelineRunId",
                table: "Releases",
                column: "CreatedByPipelineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRunArtifactInputs_ArtifactId",
                table: "PipelineRunArtifactInputs",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRunArtifactInputs_PipelineRunId",
                table: "PipelineRunArtifactInputs",
                column: "PipelineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRunArtifactInputs_ReleaseId",
                table: "PipelineRunArtifactInputs",
                column: "ReleaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Releases_PipelineRuns_CreatedByPipelineRunId",
                table: "Releases",
                column: "CreatedByPipelineRunId",
                principalTable: "PipelineRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Releases_PipelineRuns_CreatedByPipelineRunId",
                table: "Releases");

            migrationBuilder.DropTable(
                name: "PipelineRunArtifactInputs");

            migrationBuilder.DropIndex(
                name: "IX_Releases_CreatedByPipelineRunId",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "CreatedByPipelineRunId",
                table: "Releases");
        }
    }
}
