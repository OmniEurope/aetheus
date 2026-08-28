using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddAnalysisPolicyRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AnalysisPolicyRevisions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                AnalysisPolicyId = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                SnapshotJson = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisPolicyRevisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisPolicyRevisions_AnalysisPolicies_AnalysisPolicyId",
                    column: x => x.AnalysisPolicyId,
                    principalTable: "AnalysisPolicies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisPolicyRevisions_AnalysisPolicyId_Version",
            table: "AnalysisPolicyRevisions",
            columns: new[] { "AnalysisPolicyId", "Version" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AnalysisPolicyRevisions");
    }
}
