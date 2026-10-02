using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseAssuranceVerdict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssuranceGrade",
                table: "Releases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlockingTestCount",
                table: "Releases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Deployable",
                table: "Releases",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssuranceGrade",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "BlockingTestCount",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "Deployable",
                table: "Releases");
        }
    }
}
