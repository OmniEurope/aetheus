using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddServerStorageDiagnostics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "AgentInstallDirectoryBytes",
            table: "ServerMetrics",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "AgentWorkDirectoryBytes",
            table: "ServerMetrics",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<bool>(
            name: "BuildActive",
            table: "ServerMetrics",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<long>(
            name: "BuildCacheBytes",
            table: "ServerMetrics",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "BuildCacheReclaimableBytes",
            table: "ServerMetrics",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<bool>(
            name: "DeploymentOnly",
            table: "ServerMetrics",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<long>(
            name: "DockerContainersBytes",
            table: "ServerMetrics",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "DockerImagesBytes",
            table: "ServerMetrics",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "DockerVolumesBytes",
            table: "ServerMetrics",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "JournalBytes",
            table: "ServerMetrics",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "NuGetCacheBytes",
            table: "ServerMetrics",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<bool>(
            name: "StorageMaintenanceDryRun",
            table: "ServerMetrics",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AgentInstallDirectoryBytes",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "AgentWorkDirectoryBytes",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "BuildActive",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "BuildCacheBytes",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "BuildCacheReclaimableBytes",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "DeploymentOnly",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "DockerContainersBytes",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "DockerImagesBytes",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "DockerVolumesBytes",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "JournalBytes",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "NuGetCacheBytes",
            table: "ServerMetrics");

        migrationBuilder.DropColumn(
            name: "StorageMaintenanceDryRun",
            table: "ServerMetrics");
    }
}
