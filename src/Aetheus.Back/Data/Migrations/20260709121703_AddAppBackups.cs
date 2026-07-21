using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddAppBackups : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BackupPolicies",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                DbEngine = table.Column<int>(type: "integer", nullable: false),
                DbHost = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                DbPort = table.Column<int>(type: "integer", nullable: true),
                DbName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                DbUser = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                DbPasswordEncrypted = table.Column<string>(type: "text", nullable: true),
                FilePathsJson = table.Column<string>(type: "text", nullable: true),
                ScheduleCron = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                RetentionCount = table.Column<int>(type: "integer", nullable: false),
                RestoreCheckCron = table.Column<string>(type: "text", nullable: true),
                LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastRestoreCheckAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BackupPolicies", x => x.Id);
                table.ForeignKey(
                    name: "FK_BackupPolicies_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BackupPolicies_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "BackupRuns",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                BackupPolicyId = table.Column<int>(type: "integer", nullable: false),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ArchivePath = table.Column<string>(type: "text", nullable: true),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                Sha256 = table.Column<string>(type: "text", nullable: true),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RestoreCheckStatus = table.Column<int>(type: "integer", nullable: false),
                RestoreCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RestoreCheckMessage = table.Column<string>(type: "text", nullable: true),
                Message = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BackupRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_BackupRuns_BackupPolicies_BackupPolicyId",
                    column: x => x.BackupPolicyId,
                    principalTable: "BackupPolicies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BackupPolicies_ProjectId",
            table: "BackupPolicies",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_BackupPolicies_ServerId",
            table: "BackupPolicies",
            column: "ServerId");

        migrationBuilder.CreateIndex(
            name: "IX_BackupRuns_BackupPolicyId",
            table: "BackupRuns",
            column: "BackupPolicyId");

        migrationBuilder.CreateIndex(
            name: "IX_BackupRuns_BackupPolicyId_StartedAt",
            table: "BackupRuns",
            columns: new[] { "BackupPolicyId", "StartedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BackupRuns");

        migrationBuilder.DropTable(
            name: "BackupPolicies");
    }
}
