using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class VaultSecretHistoryWithoutValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangedBy",
                table: "VaultSecretVersions",
                type: "text",
                nullable: true);

            // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: data change, no schema change (recette R-287). The
            // secret history keeps the date, the author and the name, never the value. No binary reads
            // VaultSecretVersions.EncryptedValue (the history DTO never carried it), so blanking the stored
            // values is safe while the previous colour still serves. The column is dropped by the contract
            // step of a later release. Irreversible by design: Down cannot bring the values back.
            migrationBuilder.Sql("UPDATE \"VaultSecretVersions\" SET \"EncryptedValue\" = '' WHERE \"EncryptedValue\" <> '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangedBy",
                table: "VaultSecretVersions");
        }
    }
}
