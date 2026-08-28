using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddAnalysisSecurityQualityPlatform : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ScannerCapabilitiesJson",
            table: "Servers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DastAllowedHosts",
            table: "Environments",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<bool>(
            name: "DastContainsRealData",
            table: "Environments",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "DastEnabled",
            table: "Environments",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "DastIsEphemeral",
            table: "Environments",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "AnalysisFindings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                FingerprintVersion = table.Column<int>(type: "integer", nullable: false),
                RuleId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Category = table.Column<int>(type: "integer", nullable: false),
                Severity = table.Column<int>(type: "integer", nullable: false),
                Confidence = table.Column<int>(type: "integer", nullable: false),
                Cwe = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                HelpUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisFindings", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisFindings_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AnalysisFindings_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AnalysisPolicies",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: true),
                ProjectId = table.Column<int>(type: "integer", nullable: true),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Category = table.Column<int>(type: "integer", nullable: true),
                ScannerKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                RuleId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                SeverityThreshold = table.Column<int>(type: "integer", nullable: true),
                MetricKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                Operator = table.Column<int>(type: "integer", nullable: true),
                Threshold = table.Column<double>(type: "double precision", nullable: true),
                BranchPattern = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                EnvironmentPattern = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                Behavior = table.Column<int>(type: "integer", nullable: false),
                Priority = table.Column<int>(type: "integer", nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisPolicies", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisPolicies_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AnalysisPolicies_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AnalysisReports",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                PipelineRunId = table.Column<int>(type: "integer", nullable: true),
                PipelineArtifactId = table.Column<int>(type: "integer", nullable: true),
                ScannerKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ScannerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ScannerVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Category = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Format = table.Column<int>(type: "integer", nullable: false),
                ReportPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ContentSize = table.Column<long>(type: "bigint", nullable: false),
                BranchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                EnvironmentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CommitHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                StageName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                StepName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                RuleSetHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsTruncated = table.Column<bool>(type: "boolean", nullable: false),
                ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisReports", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisReports_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AnalysisReports_PipelineArtifacts_PipelineArtifactId",
                    column: x => x.PipelineArtifactId,
                    principalTable: "PipelineArtifacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AnalysisReports_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AnalysisReports_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AnalysisFindingDecisions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                AnalysisFindingId = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CreatedByUsername = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExpirationNotificationSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisFindingDecisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisFindingDecisions_AnalysisFindings_AnalysisFindingId",
                    column: x => x.AnalysisFindingId,
                    principalTable: "AnalysisFindings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AnalysisFindingDecisions_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AnalysisFindingDecisions_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AnalysisPolicyExceptions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                AnalysisPolicyId = table.Column<int>(type: "integer", nullable: true),
                AnalysisFindingId = table.Column<int>(type: "integer", nullable: true),
                Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                RuleId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                ScannerKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Category = table.Column<int>(type: "integer", nullable: true),
                BranchPattern = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                EnvironmentPattern = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CreatedByUsername = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExpirationNotificationSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisPolicyExceptions", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisPolicyExceptions_AnalysisFindings_AnalysisFindingId",
                    column: x => x.AnalysisFindingId,
                    principalTable: "AnalysisFindings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AnalysisPolicyExceptions_AnalysisPolicies_AnalysisPolicyId",
                    column: x => x.AnalysisPolicyId,
                    principalTable: "AnalysisPolicies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AnalysisPolicyExceptions_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AnalysisPolicyExceptions_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AnalysisComponents",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                AnalysisReportId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PackageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ComponentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                LicensesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                Hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                IsDirect = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisComponents", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisComponents_AnalysisReports_AnalysisReportId",
                    column: x => x.AnalysisReportId,
                    principalTable: "AnalysisReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AnalysisComponents_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AnalysisComponents_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AnalysisEvaluations",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                AnalysisReportId = table.Column<int>(type: "integer", nullable: false),
                PipelineRunId = table.Column<int>(type: "integer", nullable: true),
                BaselineRunId = table.Column<int>(type: "integer", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                PolicySnapshotJson = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                PolicySnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                BlockerCount = table.Column<int>(type: "integer", nullable: false),
                WarningCount = table.Column<int>(type: "integer", nullable: false),
                EvaluatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisEvaluations", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisEvaluations_AnalysisReports_AnalysisReportId",
                    column: x => x.AnalysisReportId,
                    principalTable: "AnalysisReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AnalysisEvaluations_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AnalysisEvaluations_PipelineRuns_BaselineRunId",
                    column: x => x.BaselineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AnalysisEvaluations_PipelineRuns_PipelineRunId",
                    column: x => x.PipelineRunId,
                    principalTable: "PipelineRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AnalysisEvaluations_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AnalysisFindingOccurrences",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                AnalysisReportId = table.Column<int>(type: "integer", nullable: false),
                AnalysisFindingId = table.Column<int>(type: "integer", nullable: false),
                LocationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ToolName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ScannerKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                RuleId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                StartLine = table.Column<int>(type: "integer", nullable: true),
                EndLine = table.Column<int>(type: "integer", nullable: true),
                Symbol = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                BranchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CommitHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                IsNew = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisFindingOccurrences", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisFindingOccurrences_AnalysisFindings_AnalysisFinding~",
                    column: x => x.AnalysisFindingId,
                    principalTable: "AnalysisFindings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AnalysisFindingOccurrences_AnalysisReports_AnalysisReportId",
                    column: x => x.AnalysisReportId,
                    principalTable: "AnalysisReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AnalysisMetrics",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                AnalysisReportId = table.Column<int>(type: "integer", nullable: false),
                Key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Value = table.Column<double>(type: "double precision", nullable: false),
                Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Language = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Symbol = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ToolName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                BaselineValue = table.Column<double>(type: "double precision", nullable: true),
                Direction = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisMetrics", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisMetrics_AnalysisReports_AnalysisReportId",
                    column: x => x.AnalysisReportId,
                    principalTable: "AnalysisReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AnalysisMetrics_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AnalysisMetrics_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AnalysisTrackingProjects",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                LastSbomReportId = table.Column<int>(type: "integer", nullable: true),
                Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ExternalProjectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ExternalProjectName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Active = table.Column<bool>(type: "boolean", nullable: false),
                SyncStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                LastSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                LastKnownVulnerabilityCount = table.Column<int>(type: "integer", nullable: false),
                LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisTrackingProjects", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisTrackingProjects_AnalysisReports_LastSbomReportId",
                    column: x => x.LastSbomReportId,
                    principalTable: "AnalysisReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AnalysisTrackingProjects_Organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AnalysisTrackingProjects_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AnalysisVulnerabilityObservations",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OrganizationId = table.Column<int>(type: "integer", nullable: false),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                AnalysisTrackingProjectId = table.Column<int>(type: "integer", nullable: false),
                AnalysisReportId = table.Column<int>(type: "integer", nullable: true),
                VulnerabilityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ComponentName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ComponentVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PackageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Severity = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                IsContinuous = table.Column<bool>(type: "boolean", nullable: false),
                ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnalysisVulnerabilityObservations", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnalysisVulnerabilityObservations_AnalysisReports_AnalysisR~",
                    column: x => x.AnalysisReportId,
                    principalTable: "AnalysisReports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_AnalysisVulnerabilityObservations_AnalysisTrackingProjects_~",
                    column: x => x.AnalysisTrackingProjectId,
                    principalTable: "AnalysisTrackingProjects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AnalysisVulnerabilityObservations_Organizations_Organizatio~",
                    column: x => x.OrganizationId,
                    principalTable: "Organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_AnalysisVulnerabilityObservations_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisComponents_AnalysisReportId",
            table: "AnalysisComponents",
            column: "AnalysisReportId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisComponents_OrganizationId",
            table: "AnalysisComponents",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisComponents_ProjectId_PackageUrl_Version",
            table: "AnalysisComponents",
            columns: new[] { "ProjectId", "PackageUrl", "Version" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisEvaluations_AnalysisReportId",
            table: "AnalysisEvaluations",
            column: "AnalysisReportId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisEvaluations_BaselineRunId",
            table: "AnalysisEvaluations",
            column: "BaselineRunId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisEvaluations_OrganizationId",
            table: "AnalysisEvaluations",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisEvaluations_PipelineRunId",
            table: "AnalysisEvaluations",
            column: "PipelineRunId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisEvaluations_ProjectId_EvaluatedAt",
            table: "AnalysisEvaluations",
            columns: new[] { "ProjectId", "EvaluatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisFindingDecisions_AnalysisFindingId_CreatedAt",
            table: "AnalysisFindingDecisions",
            columns: new[] { "AnalysisFindingId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisFindingDecisions_OrganizationId",
            table: "AnalysisFindingDecisions",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisFindingDecisions_ProjectId_ExpiresAt",
            table: "AnalysisFindingDecisions",
            columns: new[] { "ProjectId", "ExpiresAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisFindingOccurrences_AnalysisFindingId_CreatedAt",
            table: "AnalysisFindingOccurrences",
            columns: new[] { "AnalysisFindingId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisFindingOccurrences_AnalysisReportId_AnalysisFinding~",
            table: "AnalysisFindingOccurrences",
            columns: new[] { "AnalysisReportId", "AnalysisFindingId", "LocationHash" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisFindings_OrganizationId",
            table: "AnalysisFindings",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisFindings_ProjectId_Fingerprint_FingerprintVersion",
            table: "AnalysisFindings",
            columns: new[] { "ProjectId", "Fingerprint", "FingerprintVersion" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisFindings_ProjectId_LastSeenAt",
            table: "AnalysisFindings",
            columns: new[] { "ProjectId", "LastSeenAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisFindings_ProjectId_Status_Severity",
            table: "AnalysisFindings",
            columns: new[] { "ProjectId", "Status", "Severity" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisMetrics_AnalysisReportId_Key",
            table: "AnalysisMetrics",
            columns: new[] { "AnalysisReportId", "Key" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisMetrics_OrganizationId",
            table: "AnalysisMetrics",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisMetrics_ProjectId_Key_CreatedAt",
            table: "AnalysisMetrics",
            columns: new[] { "ProjectId", "Key", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisPolicies_OrganizationId",
            table: "AnalysisPolicies",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisPolicies_OrganizationId_ProjectId_Name",
            table: "AnalysisPolicies",
            columns: new[] { "OrganizationId", "ProjectId", "Name" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisPolicies_ProjectId",
            table: "AnalysisPolicies",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisPolicyExceptions_AnalysisFindingId",
            table: "AnalysisPolicyExceptions",
            column: "AnalysisFindingId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisPolicyExceptions_AnalysisPolicyId",
            table: "AnalysisPolicyExceptions",
            column: "AnalysisPolicyId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisPolicyExceptions_OrganizationId",
            table: "AnalysisPolicyExceptions",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisPolicyExceptions_ProjectId_ExpiresAt",
            table: "AnalysisPolicyExceptions",
            columns: new[] { "ProjectId", "ExpiresAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisReports_OrganizationId",
            table: "AnalysisReports",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisReports_PipelineArtifactId",
            table: "AnalysisReports",
            column: "PipelineArtifactId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisReports_PipelineRunId_ScannerKey_ContentHash",
            table: "AnalysisReports",
            columns: new[] { "PipelineRunId", "ScannerKey", "ContentHash" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisReports_ProjectId_CreatedAt",
            table: "AnalysisReports",
            columns: new[] { "ProjectId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisTrackingProjects_Active_LastSyncAt",
            table: "AnalysisTrackingProjects",
            columns: new[] { "Active", "LastSyncAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisTrackingProjects_LastSbomReportId",
            table: "AnalysisTrackingProjects",
            column: "LastSbomReportId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisTrackingProjects_OrganizationId",
            table: "AnalysisTrackingProjects",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisTrackingProjects_ProjectId",
            table: "AnalysisTrackingProjects",
            column: "ProjectId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisVulnerabilityObservations_AnalysisReportId",
            table: "AnalysisVulnerabilityObservations",
            column: "AnalysisReportId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisVulnerabilityObservations_AnalysisTrackingProjectId~",
            table: "AnalysisVulnerabilityObservations",
            columns: new[] { "AnalysisTrackingProjectId", "VulnerabilityId", "PackageUrl", "ObservedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisVulnerabilityObservations_OrganizationId",
            table: "AnalysisVulnerabilityObservations",
            column: "OrganizationId");

        migrationBuilder.CreateIndex(
            name: "IX_AnalysisVulnerabilityObservations_ProjectId_ObservedAt",
            table: "AnalysisVulnerabilityObservations",
            columns: new[] { "ProjectId", "ObservedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AnalysisComponents");

        migrationBuilder.DropTable(
            name: "AnalysisEvaluations");

        migrationBuilder.DropTable(
            name: "AnalysisFindingDecisions");

        migrationBuilder.DropTable(
            name: "AnalysisFindingOccurrences");

        migrationBuilder.DropTable(
            name: "AnalysisMetrics");

        migrationBuilder.DropTable(
            name: "AnalysisPolicyExceptions");

        migrationBuilder.DropTable(
            name: "AnalysisVulnerabilityObservations");

        migrationBuilder.DropTable(
            name: "AnalysisFindings");

        migrationBuilder.DropTable(
            name: "AnalysisPolicies");

        migrationBuilder.DropTable(
            name: "AnalysisTrackingProjects");

        migrationBuilder.DropTable(
            name: "AnalysisReports");

        migrationBuilder.DropColumn(
            name: "ScannerCapabilitiesJson",
            table: "Servers");

        migrationBuilder.DropColumn(
            name: "DastAllowedHosts",
            table: "Environments");

        migrationBuilder.DropColumn(
            name: "DastContainsRealData",
            table: "Environments");

        migrationBuilder.DropColumn(
            name: "DastEnabled",
            table: "Environments");

        migrationBuilder.DropColumn(
            name: "DastIsEphemeral",
            table: "Environments");
    }
}
