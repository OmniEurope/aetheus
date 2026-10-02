using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMetricSeriesGroupingAndKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: the old unique index keys rollup rows on
            // (app, metric, hour), which makes attribute-scoped series impossible - two routes of the
            // same metric in the same hour are now two legitimate rows. Relaxing it is safe for the
            // version still running during a blue-green cutover: that version writes one row per
            // (app, metric, hour) anyway, so it never depends on the index to stay correct, and no
            // row is deleted. Expressed as raw SQL (IF EXISTS) so a re-run cannot fail the deploy.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_AppMetricHourly_MonitoredAppId_MetricName_HourUtc\";");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "AppMetricSamples",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AttributesJson",
                table: "AppMetricHourly",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "AppMetricHourly",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "AppMetricHourly",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppMetricHourly_MonitoredAppId_MetricName_AttributesJson_Ho~",
                table: "AppMetricHourly",
                columns: new[] { "MonitoredAppId", "MetricName", "AttributesJson", "HourUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppMetricHourly_MonitoredAppId_MetricName_AttributesJson_Ho~",
                table: "AppMetricHourly");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "AppMetricSamples");

            migrationBuilder.DropColumn(
                name: "AttributesJson",
                table: "AppMetricHourly");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "AppMetricHourly");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "AppMetricHourly");

            migrationBuilder.CreateIndex(
                name: "IX_AppMetricHourly_MonitoredAppId_MetricName_HourUtc",
                table: "AppMetricHourly",
                columns: new[] { "MonitoredAppId", "MetricName", "HourUtc" },
                unique: true);
        }
    }
}
