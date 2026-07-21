using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddExternalRepoMirrorColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "GitConnectionId",
            table: "Projects",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BaseUrl",
            table: "GitConnections",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "FetchIntervalMinutes",
            table: "GitConnections",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "LastFetchError",
            table: "GitConnections",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "LastFetchedAt",
            table: "GitConnections",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MirrorPath",
            table: "GitConnections",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "MirrorStatus",
            table: "GitConnections",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<bool>(
            name: "WriteEnabled",
            table: "GitConnections",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GitConnectionId",
            table: "Projects");

        migrationBuilder.DropColumn(
            name: "BaseUrl",
            table: "GitConnections");

        migrationBuilder.DropColumn(
            name: "FetchIntervalMinutes",
            table: "GitConnections");

        migrationBuilder.DropColumn(
            name: "LastFetchError",
            table: "GitConnections");

        migrationBuilder.DropColumn(
            name: "LastFetchedAt",
            table: "GitConnections");

        migrationBuilder.DropColumn(
            name: "MirrorPath",
            table: "GitConnections");

        migrationBuilder.DropColumn(
            name: "MirrorStatus",
            table: "GitConnections");

        migrationBuilder.DropColumn(
            name: "WriteEnabled",
            table: "GitConnections");
    }
}
