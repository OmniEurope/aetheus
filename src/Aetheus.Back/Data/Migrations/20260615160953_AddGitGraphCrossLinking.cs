using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddGitGraphCrossLinking : Migration
{
    // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: idempotent data copy preserves links before the reviewed legacy contract.
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "GitBranches",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GitBranches", x => x.Id);
                table.ForeignKey(
                    name: "FK_GitBranches_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "GitCommits",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ProjectId = table.Column<int>(type: "integer", nullable: false),
                Sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GitCommits", x => x.Id);
                table.ForeignKey(
                    name: "FK_GitCommits_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ReleaseArtifacts",
            columns: table => new
            {
                ArtifactsId = table.Column<int>(type: "integer", nullable: false),
                ReleasesId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReleaseArtifacts", x => new { x.ArtifactsId, x.ReleasesId });
                table.ForeignKey(
                    name: "FK_ReleaseArtifacts_PipelineArtifacts_ArtifactsId",
                    column: x => x.ArtifactsId,
                    principalTable: "PipelineArtifacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ReleaseArtifacts_Releases_ReleasesId",
                    column: x => x.ReleasesId,
                    principalTable: "Releases",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BranchArtifacts",
            columns: table => new
            {
                ArtifactsId = table.Column<int>(type: "integer", nullable: false),
                BranchesId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BranchArtifacts", x => new { x.ArtifactsId, x.BranchesId });
                table.ForeignKey(
                    name: "FK_BranchArtifacts_GitBranches_BranchesId",
                    column: x => x.BranchesId,
                    principalTable: "GitBranches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BranchArtifacts_PipelineArtifacts_ArtifactsId",
                    column: x => x.ArtifactsId,
                    principalTable: "PipelineArtifacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BranchReleases",
            columns: table => new
            {
                BranchesId = table.Column<int>(type: "integer", nullable: false),
                ReleasesId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BranchReleases", x => new { x.BranchesId, x.ReleasesId });
                table.ForeignKey(
                    name: "FK_BranchReleases_GitBranches_BranchesId",
                    column: x => x.BranchesId,
                    principalTable: "GitBranches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BranchReleases_Releases_ReleasesId",
                    column: x => x.ReleasesId,
                    principalTable: "Releases",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "BranchCommits",
            columns: table => new
            {
                BranchesId = table.Column<int>(type: "integer", nullable: false),
                CommitsId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BranchCommits", x => new { x.BranchesId, x.CommitsId });
                table.ForeignKey(
                    name: "FK_BranchCommits_GitBranches_BranchesId",
                    column: x => x.BranchesId,
                    principalTable: "GitBranches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BranchCommits_GitCommits_CommitsId",
                    column: x => x.CommitsId,
                    principalTable: "GitCommits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CommitArtifacts",
            columns: table => new
            {
                ArtifactsId = table.Column<int>(type: "integer", nullable: false),
                CommitsId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CommitArtifacts", x => new { x.ArtifactsId, x.CommitsId });
                table.ForeignKey(
                    name: "FK_CommitArtifacts_GitCommits_CommitsId",
                    column: x => x.CommitsId,
                    principalTable: "GitCommits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CommitArtifacts_PipelineArtifacts_ArtifactsId",
                    column: x => x.ArtifactsId,
                    principalTable: "PipelineArtifacts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CommitReleases",
            columns: table => new
            {
                CommitsId = table.Column<int>(type: "integer", nullable: false),
                ReleasesId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CommitReleases", x => new { x.CommitsId, x.ReleasesId });
                table.ForeignKey(
                    name: "FK_CommitReleases_GitCommits_CommitsId",
                    column: x => x.CommitsId,
                    principalTable: "GitCommits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CommitReleases_Releases_ReleasesId",
                    column: x => x.ReleasesId,
                    principalTable: "Releases",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BranchArtifacts_BranchesId",
            table: "BranchArtifacts",
            column: "BranchesId");

        migrationBuilder.CreateIndex(
            name: "IX_BranchCommits_CommitsId",
            table: "BranchCommits",
            column: "CommitsId");

        migrationBuilder.CreateIndex(
            name: "IX_BranchReleases_ReleasesId",
            table: "BranchReleases",
            column: "ReleasesId");

        migrationBuilder.CreateIndex(
            name: "IX_CommitArtifacts_CommitsId",
            table: "CommitArtifacts",
            column: "CommitsId");

        migrationBuilder.CreateIndex(
            name: "IX_CommitReleases_ReleasesId",
            table: "CommitReleases",
            column: "ReleasesId");

        migrationBuilder.CreateIndex(
            name: "IX_GitBranches_ProjectId_Name",
            table: "GitBranches",
            columns: new[] { "ProjectId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_GitCommits_ProjectId_Sha",
            table: "GitCommits",
            columns: new[] { "ProjectId", "Sha" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ReleaseArtifacts_ReleasesId",
            table: "ReleaseArtifacts",
            column: "ReleasesId");

        // Backfill the new many-to-many graph from the existing data BEFORE dropping ReleaseId.
        // Idempotent (ON CONFLICT DO NOTHING) so re-runs against partially-migrated data are safe.
        migrationBuilder.Sql(
            """
            -- Preserve the former one-to-one release↔artifact links.
            INSERT INTO "ReleaseArtifacts" ("ArtifactsId", "ReleasesId")
            SELECT "Id", "ReleaseId" FROM "PipelineArtifacts" WHERE "ReleaseId" IS NOT NULL
            ON CONFLICT DO NOTHING;

            -- Link releases to the artifacts produced by the same pipeline run (the natural bundle).
            INSERT INTO "ReleaseArtifacts" ("ArtifactsId", "ReleasesId")
            SELECT a."Id", r."Id" FROM "Releases" r
            JOIN "PipelineArtifacts" a ON a."PipelineRunId" = r."PipelineRunId"
            WHERE r."PipelineRunId" IS NOT NULL
            ON CONFLICT DO NOTHING;

            -- Seed commits / branches from release git context.
            INSERT INTO "GitCommits" ("ProjectId", "Sha", "CreatedAt")
            SELECT DISTINCT "ProjectId", "CommitHash", now() FROM "Releases"
            WHERE "CommitHash" IS NOT NULL AND "CommitHash" <> ''
            ON CONFLICT ("ProjectId", "Sha") DO NOTHING;

            INSERT INTO "GitBranches" ("ProjectId", "Name", "CreatedAt")
            SELECT DISTINCT "ProjectId", "BranchName", now() FROM "Releases"
            WHERE "BranchName" IS NOT NULL AND "BranchName" <> ''
            ON CONFLICT ("ProjectId", "Name") DO NOTHING;

            -- Seed commits / branches from the pipeline runs that produced artifacts.
            INSERT INTO "GitCommits" ("ProjectId", "Sha", "CreatedAt")
            SELECT DISTINCT a."ProjectId", pr."CommitHash", now()
            FROM "PipelineArtifacts" a JOIN "PipelineRuns" pr ON pr."Id" = a."PipelineRunId"
            WHERE a."ProjectId" IS NOT NULL AND pr."CommitHash" IS NOT NULL AND pr."CommitHash" <> ''
            ON CONFLICT ("ProjectId", "Sha") DO NOTHING;

            INSERT INTO "GitBranches" ("ProjectId", "Name", "CreatedAt")
            SELECT DISTINCT a."ProjectId", pr."BranchName", now()
            FROM "PipelineArtifacts" a JOIN "PipelineRuns" pr ON pr."Id" = a."PipelineRunId"
            WHERE a."ProjectId" IS NOT NULL AND pr."BranchName" IS NOT NULL AND pr."BranchName" <> ''
            ON CONFLICT ("ProjectId", "Name") DO NOTHING;

            -- Link commit/branch ↔ release.
            INSERT INTO "CommitReleases" ("CommitsId", "ReleasesId")
            SELECT gc."Id", r."Id" FROM "Releases" r
            JOIN "GitCommits" gc ON gc."ProjectId" = r."ProjectId" AND gc."Sha" = r."CommitHash"
            WHERE r."CommitHash" IS NOT NULL AND r."CommitHash" <> ''
            ON CONFLICT DO NOTHING;

            INSERT INTO "BranchReleases" ("BranchesId", "ReleasesId")
            SELECT gb."Id", r."Id" FROM "Releases" r
            JOIN "GitBranches" gb ON gb."ProjectId" = r."ProjectId" AND gb."Name" = r."BranchName"
            WHERE r."BranchName" IS NOT NULL AND r."BranchName" <> ''
            ON CONFLICT DO NOTHING;

            -- Link commit/branch ↔ artifact via the producing run.
            INSERT INTO "CommitArtifacts" ("ArtifactsId", "CommitsId")
            SELECT a."Id", gc."Id" FROM "PipelineArtifacts" a
            JOIN "PipelineRuns" pr ON pr."Id" = a."PipelineRunId"
            JOIN "GitCommits" gc ON gc."ProjectId" = a."ProjectId" AND gc."Sha" = pr."CommitHash"
            WHERE a."ProjectId" IS NOT NULL AND pr."CommitHash" IS NOT NULL AND pr."CommitHash" <> ''
            ON CONFLICT DO NOTHING;

            INSERT INTO "BranchArtifacts" ("ArtifactsId", "BranchesId")
            SELECT a."Id", gb."Id" FROM "PipelineArtifacts" a
            JOIN "PipelineRuns" pr ON pr."Id" = a."PipelineRunId"
            JOIN "GitBranches" gb ON gb."ProjectId" = a."ProjectId" AND gb."Name" = pr."BranchName"
            WHERE a."ProjectId" IS NOT NULL AND pr."BranchName" IS NOT NULL AND pr."BranchName" <> ''
            ON CONFLICT DO NOTHING;

            -- Link commit ↔ branch where they co-occur on a release.
            INSERT INTO "BranchCommits" ("BranchesId", "CommitsId")
            SELECT DISTINCT gb."Id", gc."Id" FROM "Releases" r
            JOIN "GitCommits" gc ON gc."ProjectId" = r."ProjectId" AND gc."Sha" = r."CommitHash"
            JOIN "GitBranches" gb ON gb."ProjectId" = r."ProjectId" AND gb."Name" = r."BranchName"
            WHERE r."CommitHash" IS NOT NULL AND r."CommitHash" <> ''
              AND r."BranchName" IS NOT NULL AND r."BranchName" <> ''
            ON CONFLICT DO NOTHING;
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_PipelineArtifacts_Releases_ReleaseId",
            table: "PipelineArtifacts");

        migrationBuilder.DropIndex(
            name: "IX_PipelineArtifacts_ReleaseId",
            table: "PipelineArtifacts");

        migrationBuilder.DropColumn(
            name: "ReleaseId",
            table: "PipelineArtifacts");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BranchArtifacts");

        migrationBuilder.DropTable(
            name: "BranchCommits");

        migrationBuilder.DropTable(
            name: "BranchReleases");

        migrationBuilder.DropTable(
            name: "CommitArtifacts");

        migrationBuilder.DropTable(
            name: "CommitReleases");

        migrationBuilder.DropTable(
            name: "ReleaseArtifacts");

        migrationBuilder.DropTable(
            name: "GitBranches");

        migrationBuilder.DropTable(
            name: "GitCommits");

        migrationBuilder.AddColumn<int>(
            name: "ReleaseId",
            table: "PipelineArtifacts",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PipelineArtifacts_ReleaseId",
            table: "PipelineArtifacts",
            column: "ReleaseId",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_PipelineArtifacts_Releases_ReleaseId",
            table: "PipelineArtifacts",
            column: "ReleaseId",
            principalTable: "Releases",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }
}
