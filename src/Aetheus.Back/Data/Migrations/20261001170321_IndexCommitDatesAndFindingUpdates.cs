using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class IndexCommitDatesAndFindingUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_GitCommits_ProjectId_CommittedAt_Id",
                table: "GitCommits",
                columns: new[] { "ProjectId", "CommittedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GitCommits_ProjectId_CreatedAt_Id",
                table: "GitCommits",
                columns: new[] { "ProjectId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisFindings_ProjectId_UpdatedAt",
                table: "AnalysisFindings",
                columns: new[] { "ProjectId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GitCommits_ProjectId_CommittedAt_Id",
                table: "GitCommits");

            migrationBuilder.DropIndex(
                name: "IX_GitCommits_ProjectId_CreatedAt_Id",
                table: "GitCommits");

            migrationBuilder.DropIndex(
                name: "IX_AnalysisFindings_ProjectId_UpdatedAt",
                table: "AnalysisFindings");
        }
    }
}
