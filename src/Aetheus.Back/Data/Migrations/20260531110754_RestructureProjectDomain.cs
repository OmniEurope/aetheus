using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class RestructureProjectDomain : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Vaults_Name_ProjectId",
            table: "Vaults");

        migrationBuilder.DropIndex(
            name: "IX_VariableLibraries_Name_ProjectId",
            table: "VariableLibraries");

        migrationBuilder.DropIndex(
            name: "IX_Pipelines_Name",
            table: "Pipelines");

        migrationBuilder.DropIndex(
            name: "IX_Environments_Name",
            table: "Environments");

        migrationBuilder.AddColumn<int>(
            name: "EnvironmentId",
            table: "Vaults",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ProjectServerId",
            table: "Vaults",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "EnvironmentId",
            table: "VariableLibraries",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ProjectServerId",
            table: "VariableLibraries",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "EnvironmentId",
            table: "Pipelines",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ProjectServerId",
            table: "Pipelines",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "EnvironmentProjectServers",
            columns: table => new
            {
                EnvironmentId = table.Column<int>(type: "integer", nullable: false),
                ProjectServerId = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EnvironmentProjectServers", x => new { x.EnvironmentId, x.ProjectServerId });
                table.ForeignKey(
                    name: "FK_EnvironmentProjectServers_Environments_EnvironmentId",
                    column: x => x.EnvironmentId,
                    principalTable: "Environments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_EnvironmentProjectServers_ProjectServers_ProjectServerId",
                    column: x => x.ProjectServerId,
                    principalTable: "ProjectServers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Vaults_EnvironmentId",
            table: "Vaults",
            column: "EnvironmentId");

        migrationBuilder.CreateIndex(
            name: "IX_Vaults_Name_EnvironmentId",
            table: "Vaults",
            columns: new[] { "Name", "EnvironmentId" },
            unique: true,
            filter: "\"EnvironmentId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Vaults_Name_ProjectId",
            table: "Vaults",
            columns: new[] { "Name", "ProjectId" },
            unique: true,
            filter: "\"ProjectId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Vaults_Name_ProjectServerId",
            table: "Vaults",
            columns: new[] { "Name", "ProjectServerId" },
            unique: true,
            filter: "\"ProjectServerId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Vaults_ProjectServerId",
            table: "Vaults",
            column: "ProjectServerId");

        migrationBuilder.CreateIndex(
            name: "IX_VariableLibraries_EnvironmentId",
            table: "VariableLibraries",
            column: "EnvironmentId");

        migrationBuilder.CreateIndex(
            name: "IX_VariableLibraries_Name_EnvironmentId",
            table: "VariableLibraries",
            columns: new[] { "Name", "EnvironmentId" },
            unique: true,
            filter: "\"EnvironmentId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_VariableLibraries_Name_ProjectId",
            table: "VariableLibraries",
            columns: new[] { "Name", "ProjectId" },
            unique: true,
            filter: "\"ProjectId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_VariableLibraries_Name_ProjectServerId",
            table: "VariableLibraries",
            columns: new[] { "Name", "ProjectServerId" },
            unique: true,
            filter: "\"ProjectServerId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_VariableLibraries_ProjectServerId",
            table: "VariableLibraries",
            column: "ProjectServerId");

        migrationBuilder.CreateIndex(
            name: "IX_Pipelines_EnvironmentId",
            table: "Pipelines",
            column: "EnvironmentId");

        migrationBuilder.CreateIndex(
            name: "IX_Pipelines_Name_EnvironmentId",
            table: "Pipelines",
            columns: new[] { "Name", "EnvironmentId" },
            unique: true,
            filter: "\"EnvironmentId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Pipelines_Name_ProjectId",
            table: "Pipelines",
            columns: new[] { "Name", "ProjectId" },
            unique: true,
            filter: "\"ProjectId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Pipelines_Name_ProjectServerId",
            table: "Pipelines",
            columns: new[] { "Name", "ProjectServerId" },
            unique: true,
            filter: "\"ProjectServerId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Pipelines_ProjectServerId",
            table: "Pipelines",
            column: "ProjectServerId");

        migrationBuilder.CreateIndex(
            name: "IX_Environments_Name_ProjectId",
            table: "Environments",
            columns: new[] { "Name", "ProjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EnvironmentProjectServers_ProjectServerId",
            table: "EnvironmentProjectServers",
            column: "ProjectServerId");

        migrationBuilder.AddForeignKey(
            name: "FK_Pipelines_Environments_EnvironmentId",
            table: "Pipelines",
            column: "EnvironmentId",
            principalTable: "Environments",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_Pipelines_ProjectServers_ProjectServerId",
            table: "Pipelines",
            column: "ProjectServerId",
            principalTable: "ProjectServers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_VariableLibraries_Environments_EnvironmentId",
            table: "VariableLibraries",
            column: "EnvironmentId",
            principalTable: "Environments",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_VariableLibraries_ProjectServers_ProjectServerId",
            table: "VariableLibraries",
            column: "ProjectServerId",
            principalTable: "ProjectServers",
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
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Pipelines_Environments_EnvironmentId",
            table: "Pipelines");

        migrationBuilder.DropForeignKey(
            name: "FK_Pipelines_ProjectServers_ProjectServerId",
            table: "Pipelines");

        migrationBuilder.DropForeignKey(
            name: "FK_VariableLibraries_Environments_EnvironmentId",
            table: "VariableLibraries");

        migrationBuilder.DropForeignKey(
            name: "FK_VariableLibraries_ProjectServers_ProjectServerId",
            table: "VariableLibraries");

        migrationBuilder.DropForeignKey(
            name: "FK_Vaults_Environments_EnvironmentId",
            table: "Vaults");

        migrationBuilder.DropForeignKey(
            name: "FK_Vaults_ProjectServers_ProjectServerId",
            table: "Vaults");

        migrationBuilder.DropTable(
            name: "EnvironmentProjectServers");

        migrationBuilder.DropIndex(
            name: "IX_Vaults_EnvironmentId",
            table: "Vaults");

        migrationBuilder.DropIndex(
            name: "IX_Vaults_Name_EnvironmentId",
            table: "Vaults");

        migrationBuilder.DropIndex(
            name: "IX_Vaults_Name_ProjectId",
            table: "Vaults");

        migrationBuilder.DropIndex(
            name: "IX_Vaults_Name_ProjectServerId",
            table: "Vaults");

        migrationBuilder.DropIndex(
            name: "IX_Vaults_ProjectServerId",
            table: "Vaults");

        migrationBuilder.DropIndex(
            name: "IX_VariableLibraries_EnvironmentId",
            table: "VariableLibraries");

        migrationBuilder.DropIndex(
            name: "IX_VariableLibraries_Name_EnvironmentId",
            table: "VariableLibraries");

        migrationBuilder.DropIndex(
            name: "IX_VariableLibraries_Name_ProjectId",
            table: "VariableLibraries");

        migrationBuilder.DropIndex(
            name: "IX_VariableLibraries_Name_ProjectServerId",
            table: "VariableLibraries");

        migrationBuilder.DropIndex(
            name: "IX_VariableLibraries_ProjectServerId",
            table: "VariableLibraries");

        migrationBuilder.DropIndex(
            name: "IX_Pipelines_EnvironmentId",
            table: "Pipelines");

        migrationBuilder.DropIndex(
            name: "IX_Pipelines_Name_EnvironmentId",
            table: "Pipelines");

        migrationBuilder.DropIndex(
            name: "IX_Pipelines_Name_ProjectId",
            table: "Pipelines");

        migrationBuilder.DropIndex(
            name: "IX_Pipelines_Name_ProjectServerId",
            table: "Pipelines");

        migrationBuilder.DropIndex(
            name: "IX_Pipelines_ProjectServerId",
            table: "Pipelines");

        migrationBuilder.DropIndex(
            name: "IX_Environments_Name_ProjectId",
            table: "Environments");

        migrationBuilder.DropColumn(
            name: "EnvironmentId",
            table: "Vaults");

        migrationBuilder.DropColumn(
            name: "ProjectServerId",
            table: "Vaults");

        migrationBuilder.DropColumn(
            name: "EnvironmentId",
            table: "VariableLibraries");

        migrationBuilder.DropColumn(
            name: "ProjectServerId",
            table: "VariableLibraries");

        migrationBuilder.DropColumn(
            name: "EnvironmentId",
            table: "Pipelines");

        migrationBuilder.DropColumn(
            name: "ProjectServerId",
            table: "Pipelines");

        migrationBuilder.CreateIndex(
            name: "IX_Vaults_Name_ProjectId",
            table: "Vaults",
            columns: new[] { "Name", "ProjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_VariableLibraries_Name_ProjectId",
            table: "VariableLibraries",
            columns: new[] { "Name", "ProjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Pipelines_Name",
            table: "Pipelines",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Environments_Name",
            table: "Environments",
            column: "Name",
            unique: true);
    }
}
