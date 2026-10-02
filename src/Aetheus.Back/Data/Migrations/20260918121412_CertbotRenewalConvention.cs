using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class CertbotRenewalConvention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Authenticator",
                table: "CertbotCertificates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RenewalConvention",
                table: "CertbotCertificates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WebrootPath",
                table: "CertbotCertificates",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CertbotStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServerId = table.Column<int>(type: "integer", nullable: false),
                    RenewalCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RenewalCheckSucceeded = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertbotStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertbotStates_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CertbotStates_ServerId",
                table: "CertbotStates",
                column: "ServerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CertbotStates");

            migrationBuilder.DropColumn(
                name: "Authenticator",
                table: "CertbotCertificates");

            migrationBuilder.DropColumn(
                name: "RenewalConvention",
                table: "CertbotCertificates");

            migrationBuilder.DropColumn(
                name: "WebrootPath",
                table: "CertbotCertificates");
        }
    }
}
