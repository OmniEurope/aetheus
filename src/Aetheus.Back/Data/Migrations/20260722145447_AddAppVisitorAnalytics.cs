using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddAppVisitorAnalytics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AppVisitorIdentities",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                MonitoredAppId = table.Column<int>(type: "integer", nullable: false),
                DayUtc = table.Column<DateOnly>(type: "date", nullable: false),
                FingerprintHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppVisitorIdentities", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppVisitorIdentities_MonitoredApps_MonitoredAppId",
                    column: x => x.MonitoredAppId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AppVisitorIdentities_DayUtc",
            table: "AppVisitorIdentities",
            column: "DayUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AppVisitorIdentities_MonitoredAppId_DayUtc_FingerprintHash",
            table: "AppVisitorIdentities",
            columns: new[] { "MonitoredAppId", "DayUtc", "FingerprintHash" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppVisitorIdentities");
    }
}
