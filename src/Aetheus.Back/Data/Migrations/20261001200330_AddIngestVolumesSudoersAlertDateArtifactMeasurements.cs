using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIngestVolumesSudoersAlertDateArtifactMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SudoersDriftAlertedAt",
                table: "Servers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppAnalyticsIngestVolumes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppAnalyticsIngestVolumes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppAnalyticsIngestVolumes_MonitoredApps_MonitoredAppId",
                        column: x => x.MonitoredAppId,
                        principalTable: "MonitoredApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArtifactStorageMeasurements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MeasuredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactStorageMeasurements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppAnalyticsIngestVolumes_MonitoredAppId_ReceivedAtUtc",
                table: "AppAnalyticsIngestVolumes",
                columns: new[] { "MonitoredAppId", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAnalyticsIngestVolumes_ReceivedAtUtc",
                table: "AppAnalyticsIngestVolumes",
                column: "ReceivedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppAnalyticsIngestVolumes");

            migrationBuilder.DropTable(
                name: "ArtifactStorageMeasurements");

            migrationBuilder.DropColumn(
                name: "SudoersDriftAlertedAt",
                table: "Servers");
        }
    }
}
