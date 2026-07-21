using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddTeamspeakClientsAndBans : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Platform",
            table: "TeamspeakStates",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateTable(
            name: "TeamspeakBans",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                BanId = table.Column<int>(type: "integer", nullable: false),
                Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UniqueId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Nickname = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Duration = table.Column<long>(type: "bigint", nullable: false),
                Created = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TeamspeakBans", x => x.Id);
                table.ForeignKey(
                    name: "FK_TeamspeakBans_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TeamspeakClients",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ServerId = table.Column<int>(type: "integer", nullable: false),
                ClientId = table.Column<int>(type: "integer", nullable: false),
                UniqueId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Nickname = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ChannelId = table.Column<int>(type: "integer", nullable: false),
                ChannelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                IdleTimeSeconds = table.Column<long>(type: "bigint", nullable: false),
                ConnectionTimeSeconds = table.Column<long>(type: "bigint", nullable: false),
                IsServerQuery = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TeamspeakClients", x => x.Id);
                table.ForeignKey(
                    name: "FK_TeamspeakClients_Servers_ServerId",
                    column: x => x.ServerId,
                    principalTable: "Servers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TeamspeakBans_ServerId_BanId",
            table: "TeamspeakBans",
            columns: new[] { "ServerId", "BanId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TeamspeakBans_ServerId_Nickname",
            table: "TeamspeakBans",
            columns: new[] { "ServerId", "Nickname" });

        migrationBuilder.CreateIndex(
            name: "IX_TeamspeakClients_ServerId_ClientId",
            table: "TeamspeakClients",
            columns: new[] { "ServerId", "ClientId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TeamspeakClients_ServerId_Nickname",
            table: "TeamspeakClients",
            columns: new[] { "ServerId", "Nickname" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TeamspeakBans");

        migrationBuilder.DropTable(
            name: "TeamspeakClients");

        migrationBuilder.DropColumn(
            name: "Platform",
            table: "TeamspeakStates");
    }
}
