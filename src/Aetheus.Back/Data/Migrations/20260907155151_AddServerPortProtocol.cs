using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServerPortProtocol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED: the old unique index keys a reservation on
            // (server, port, source), which makes 53/udp and 53/tcp collide although they are two
            // different ports. Relaxing it is safe for the version still running during a blue-green
            // cutover: that version only ever writes TCP rows, so it never depends on the index to
            // stay correct, and the stricter index created below still covers exactly its rows. No
            // data is removed. Expressed as raw SQL (IF EXISTS) so a re-run cannot fail the deploy.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_ServerPortReservations_ServerId_Port_Source\";");

            // 'tcp', not the empty string EF defaults to: the column is part of the unique key below,
            // and existing rows all describe TCP claims. An empty protocol would let the same
            // (server, port, source) be written twice, once as '' and once as 'tcp'.
            migrationBuilder.AddColumn<string>(
                name: "Protocol",
                table: "ServerPortReservations",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "tcp");

            migrationBuilder.CreateIndex(
                name: "IX_ServerPortReservations_ServerId_Port_Protocol_Source",
                table: "ServerPortReservations",
                columns: new[] { "ServerId", "Port", "Protocol", "Source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServerPortReservations_ServerId_Port_Protocol_Source",
                table: "ServerPortReservations");

            // UDP sightings have no place in a schema without the column, and keeping them would make
            // the old unique index below fail on a (server, port, source) pair now held twice. They are
            // observations, so the next scan puts back whatever is still listening.
            migrationBuilder.Sql(
                "DELETE FROM \"ServerPortReservations\" WHERE \"Protocol\" = 'udp';");

            migrationBuilder.DropColumn(
                name: "Protocol",
                table: "ServerPortReservations");

            migrationBuilder.CreateIndex(
                name: "IX_ServerPortReservations_ServerId_Port_Source",
                table: "ServerPortReservations",
                columns: new[] { "ServerId", "Port", "Source" },
                unique: true);
        }
    }
}
