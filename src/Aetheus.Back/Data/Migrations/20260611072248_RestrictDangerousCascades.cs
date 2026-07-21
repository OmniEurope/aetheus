using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class RestrictDangerousCascades : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_PipelineApprovals_Environments_EnvironmentId",
            table: "PipelineApprovals");

        migrationBuilder.DropForeignKey(
            name: "FK_ProjectServers_Projects_ProjectId",
            table: "ProjectServers");

        migrationBuilder.DropForeignKey(
            name: "FK_ProjectServers_Servers_ServerId",
            table: "ProjectServers");

        migrationBuilder.DropForeignKey(
            name: "FK_Vaults_Environments_EnvironmentId",
            table: "Vaults");

        migrationBuilder.DropForeignKey(
            name: "FK_Vaults_ProjectServers_ProjectServerId",
            table: "Vaults");

        migrationBuilder.DropForeignKey(
            name: "FK_Vaults_Projects_ProjectId",
            table: "Vaults");

        migrationBuilder.AddForeignKey(
            name: "FK_PipelineApprovals_Environments_EnvironmentId",
            table: "PipelineApprovals",
            column: "EnvironmentId",
            principalTable: "Environments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_ProjectServers_Projects_ProjectId",
            table: "ProjectServers",
            column: "ProjectId",
            principalTable: "Projects",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_ProjectServers_Servers_ServerId",
            table: "ProjectServers",
            column: "ServerId",
            principalTable: "Servers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Vaults_Environments_EnvironmentId",
            table: "Vaults",
            column: "EnvironmentId",
            principalTable: "Environments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Vaults_ProjectServers_ProjectServerId",
            table: "Vaults",
            column: "ProjectServerId",
            principalTable: "ProjectServers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Vaults_Projects_ProjectId",
            table: "Vaults",
            column: "ProjectId",
            principalTable: "Projects",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_PipelineApprovals_Environments_EnvironmentId",
            table: "PipelineApprovals");

        migrationBuilder.DropForeignKey(
            name: "FK_ProjectServers_Projects_ProjectId",
            table: "ProjectServers");

        migrationBuilder.DropForeignKey(
            name: "FK_ProjectServers_Servers_ServerId",
            table: "ProjectServers");

        migrationBuilder.DropForeignKey(
            name: "FK_Vaults_Environments_EnvironmentId",
            table: "Vaults");

        migrationBuilder.DropForeignKey(
            name: "FK_Vaults_ProjectServers_ProjectServerId",
            table: "Vaults");

        migrationBuilder.DropForeignKey(
            name: "FK_Vaults_Projects_ProjectId",
            table: "Vaults");

        migrationBuilder.AddForeignKey(
            name: "FK_PipelineApprovals_Environments_EnvironmentId",
            table: "PipelineApprovals",
            column: "EnvironmentId",
            principalTable: "Environments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_ProjectServers_Projects_ProjectId",
            table: "ProjectServers",
            column: "ProjectId",
            principalTable: "Projects",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_ProjectServers_Servers_ServerId",
            table: "ProjectServers",
            column: "ServerId",
            principalTable: "Servers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_Vaults_Environments_EnvironmentId",
            table: "Vaults",
            column: "EnvironmentId",
            principalTable: "Environments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_Vaults_ProjectServers_ProjectServerId",
            table: "Vaults",
            column: "ProjectServerId",
            principalTable: "ProjectServers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_Vaults_Projects_ProjectId",
            table: "Vaults",
            column: "ProjectId",
            principalTable: "Projects",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
