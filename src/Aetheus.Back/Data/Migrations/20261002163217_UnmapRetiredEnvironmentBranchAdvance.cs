using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <summary>
    /// Recette R2-001, contract step 1 of 2: the model stops mapping Environments.AdvanceBranchOnDeploy
    /// and Environments.AdvanceBranchName, the database keeps them. The previous colour (V-1) still
    /// selects both columns and shares this database, so dropping them now would break it and the QA
    /// rollback gate (V and V-1 only). Inserts from this version omit them safely: AdvanceBranchOnDeploy
    /// has a database default of false and AdvanceBranchName is nullable. Step 2, a later release whose
    /// V-1 is this one, drops the columns (docs/contracts/deployment.md, pending contract steps).
    /// </summary>
    public partial class UnmapRetiredEnvironmentBranchAdvance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
