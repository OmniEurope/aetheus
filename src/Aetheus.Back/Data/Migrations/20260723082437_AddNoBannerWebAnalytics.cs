using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddNoBannerWebAnalytics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AnalyticsAllowedOriginsJson",
            table: "MonitoredApps",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "AnalyticsEnabled",
            table: "MonitoredApps",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "AnalyticsLastIngestAt",
            table: "MonitoredApps",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "AnalyticsPseudonymKeyCreatedAt",
            table: "MonitoredApps",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AnalyticsPseudonymKeyVersion",
            table: "MonitoredApps",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<bool>(
            name: "AnalyticsPublicIngestEnabled",
            table: "MonitoredApps",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "AnalyticsQuotaAlertLevel",
            table: "MonitoredApps",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<long>(
            name: "AnalyticsRejectedCount",
            table: "MonitoredApps",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<string>(
            name: "AnalyticsSiteId",
            table: "MonitoredApps",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "AnalyticsStorageBudgetBytes",
            table: "MonitoredApps",
            type: "bigint",
            nullable: false,
            defaultValue: 104857600L);

        migrationBuilder.AddColumn<string>(
            name: "AnalyticsVaultName",
            table: "MonitoredApps",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "IngestKeyVersion",
            table: "MonitoredApps",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "PreviousIngestKeyHash",
            table: "MonitoredApps",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "PreviousIngestKeyValidUntil",
            table: "MonitoredApps",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "AppAnalyticsAggregates",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                PeriodKind = table.Column<int>(type: "integer", nullable: false),
                PeriodStartUtc = table.Column<DateOnly>(type: "date", nullable: false),
                UniqueVisitors = table.Column<int>(type: "integer", nullable: false),
                AuthenticatedUniqueVisitors = table.Column<int>(type: "integer", nullable: false),
                Sessions = table.Column<int>(type: "integer", nullable: false),
                ReturningVisitors = table.Column<int>(type: "integer", nullable: false),
                PageViews = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppAnalyticsAggregates", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppAnalyticsAggregates_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AppAnalyticsEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Route = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                SessionPseudonym = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Authenticated = table.Column<bool>(type: "boolean", nullable: false),
                KeyVersion = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppAnalyticsEvents", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppAnalyticsEvents_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AppAnalyticsPageAggregates",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                DayUtc = table.Column<DateOnly>(type: "date", nullable: false),
                Route = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                PageViews = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppAnalyticsPageAggregates", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppAnalyticsPageAggregates_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AppAnalyticsPeriodIdentities",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                PeriodKind = table.Column<int>(type: "integer", nullable: false),
                PeriodStartUtc = table.Column<DateOnly>(type: "date", nullable: false),
                Pseudonym = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                KeyVersion = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppAnalyticsPeriodIdentities", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppAnalyticsPeriodIdentities_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AppAnalyticsRejections",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppAnalyticsRejections", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppAnalyticsRejections_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AppAnalyticsSessions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                SessionPseudonym = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PageViewCount = table.Column<int>(type: "integer", nullable: false),
                Authenticated = table.Column<bool>(type: "boolean", nullable: false),
                ReturningVisitor = table.Column<bool>(type: "boolean", nullable: false),
                KeyVersion = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppAnalyticsSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppAnalyticsSessions_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MonitoredApps_AnalyticsSiteId",
            table: "MonitoredApps",
            column: "AnalyticsSiteId",
            unique: true,
            filter: "\"AnalyticsSiteId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_MonitoredApps_PreviousIngestKeyHash",
            table: "MonitoredApps",
            column: "PreviousIngestKeyHash",
            unique: true,
            filter: "\"PreviousIngestKeyHash\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsAggregates_MonitoredAppId_PeriodKind_PeriodStar~",
            table: "AppAnalyticsAggregates",
            columns: new[] { "MonitoredAppId", "PeriodKind", "PeriodStartUtc" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsAggregates_PeriodStartUtc",
            table: "AppAnalyticsAggregates",
            column: "PeriodStartUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsEvents_MonitoredAppId_EventId",
            table: "AppAnalyticsEvents",
            columns: new[] { "MonitoredAppId", "EventId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsEvents_MonitoredAppId_OccurredAtUtc",
            table: "AppAnalyticsEvents",
            columns: new[] { "MonitoredAppId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsPageAggregates_DayUtc",
            table: "AppAnalyticsPageAggregates",
            column: "DayUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsPageAggregates_MonitoredAppId_DayUtc_Route",
            table: "AppAnalyticsPageAggregates",
            columns: new[] { "MonitoredAppId", "DayUtc", "Route" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsPeriodIdentities_LastSeenAtUtc",
            table: "AppAnalyticsPeriodIdentities",
            column: "LastSeenAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsPeriodIdentities_MonitoredAppId_PeriodKind_Peri~",
            table: "AppAnalyticsPeriodIdentities",
            columns: new[] { "MonitoredAppId", "PeriodKind", "PeriodStartUtc", "Pseudonym" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsRejections_MonitoredAppId",
            table: "AppAnalyticsRejections",
            column: "MonitoredAppId");

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsRejections_OccurredAtUtc",
            table: "AppAnalyticsRejections",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsSessions_LastSeenAtUtc",
            table: "AppAnalyticsSessions",
            column: "LastSeenAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AppAnalyticsSessions_MonitoredAppId_SessionPseudonym_LastSe~",
            table: "AppAnalyticsSessions",
            columns: new[] { "MonitoredAppId", "SessionPseudonym", "LastSeenAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppAnalyticsAggregates");

        migrationBuilder.DropTable(
            name: "AppAnalyticsEvents");

        migrationBuilder.DropTable(
            name: "AppAnalyticsPageAggregates");

        migrationBuilder.DropTable(
            name: "AppAnalyticsPeriodIdentities");

        migrationBuilder.DropTable(
            name: "AppAnalyticsRejections");

        migrationBuilder.DropTable(
            name: "AppAnalyticsSessions");

        migrationBuilder.DropIndex(
            name: "IX_MonitoredApps_AnalyticsSiteId",
            table: "MonitoredApps");

        migrationBuilder.DropIndex(
            name: "IX_MonitoredApps_PreviousIngestKeyHash",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsAllowedOriginsJson",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsEnabled",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsLastIngestAt",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsPseudonymKeyCreatedAt",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsPseudonymKeyVersion",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsPublicIngestEnabled",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsQuotaAlertLevel",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsRejectedCount",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsSiteId",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsStorageBudgetBytes",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "AnalyticsVaultName",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "IngestKeyVersion",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "PreviousIngestKeyHash",
            table: "MonitoredApps");

        migrationBuilder.DropColumn(
            name: "PreviousIngestKeyValidUntil",
            table: "MonitoredApps");
    }
}
