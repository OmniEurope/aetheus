using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppMetricNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppMetricNames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMetricNames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppMetricNames_MonitoredApps_MonitoredAppId",
                        column: x => x.MonitoredAppId,
                        principalTable: "MonitoredApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppMetricHourly_HourUtc",
                table: "AppMetricHourly",
                column: "HourUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AppMetricNames_MonitoredAppId_Name",
                table: "AppMetricNames",
                columns: new[] { "MonitoredAppId", "Name" },
                unique: true);

            // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: backfill of the table created just above, no schema
            // change and nothing the previous colour reads or writes (it does not know the table).
            // The names the applications already send, read once from their samples: without it the
            // metric explorer would list nothing until each name is ingested again. A name the previous
            // colour ingests for the first time while both run is not written here; the new colour
            // records it at its own first batch carrying that name.
            migrationBuilder.Sql(
                "INSERT INTO \"AppMetricNames\" (\"MonitoredAppId\", \"Name\") " +
                "SELECT DISTINCT \"MonitoredAppId\", \"MetricName\" FROM \"AppMetricSamples\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMetricNames");

            migrationBuilder.DropIndex(
                name: "IX_AppMetricHourly_HourUtc",
                table: "AppMetricHourly");
        }
    }
}
