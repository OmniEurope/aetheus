using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddPackageRegistry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RegistryPackages",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Kind = table.Column<int>(type: "integer", nullable: false),
                Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                NormalizedName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                DistTagsJson = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RegistryPackages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "RegistryPackageVersions",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RegistryPackageId = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                NormalizedVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Sha1 = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Integrity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Metadata = table.Column<string>(type: "text", nullable: false),
                IsPrerelease = table.Column<bool>(type: "boolean", nullable: false),
                IsListed = table.Column<bool>(type: "boolean", nullable: false),
                PublishedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RegistryPackageVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_RegistryPackageVersions_RegistryPackages_RegistryPackageId",
                    column: x => x.RegistryPackageId,
                    principalTable: "RegistryPackages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RegistryPackages_Kind_NormalizedName",
            table: "RegistryPackages",
            columns: new[] { "Kind", "NormalizedName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RegistryPackageVersions_RegistryPackageId_NormalizedVersion",
            table: "RegistryPackageVersions",
            columns: new[] { "RegistryPackageId", "NormalizedVersion" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RegistryPackageVersions");

        migrationBuilder.DropTable(
            name: "RegistryPackages");
    }
}
