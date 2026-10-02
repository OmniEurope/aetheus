using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMailStackInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiagnosticsJson",
                table: "MailStates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HelperVersion",
                table: "MailStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Hostname",
                table: "MailStates",
                type: "character varying(253)",
                maxLength: 253,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsManagedByAetheus",
                table: "MailStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOpenDkimRunning",
                table: "MailStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSpamFilterInstalled",
                table: "MailStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSpamFilterRunning",
                table: "MailStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "SpamAddHeaderScore",
                table: "MailStates",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpamFilterName",
                table: "MailStates",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SpamFilterVersion",
                table: "MailStates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "SpamGreylistScore",
                table: "MailStates",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SpamRejectScore",
                table: "MailStates",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TlsCertPath",
                table: "MailStates",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "TlsExpiresAt",
                table: "MailStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TlsIsReadable",
                table: "MailStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TlsIsSelfSigned",
                table: "MailStates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TlsIssuer",
                table: "MailStates",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TlsSubject",
                table: "MailStates",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DkimPublicKey",
                table: "MailDomains",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DnsCheckedAt",
                table: "MailDomains",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MissingSince",
                table: "MailDomains",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "MailDomains",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MissingSince",
                table: "MailAliases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "MailAliases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MissingSince",
                table: "MailAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "MailAccounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UsageMeasuredAt",
                table: "MailAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UsedMb",
                table: "MailAccounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiagnosticsJson",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "HelperVersion",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "Hostname",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "IsManagedByAetheus",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "IsOpenDkimRunning",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "IsSpamFilterInstalled",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "IsSpamFilterRunning",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "SpamAddHeaderScore",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "SpamFilterName",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "SpamFilterVersion",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "SpamGreylistScore",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "SpamRejectScore",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "TlsCertPath",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "TlsExpiresAt",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "TlsIsReadable",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "TlsIsSelfSigned",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "TlsIssuer",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "TlsSubject",
                table: "MailStates");

            migrationBuilder.DropColumn(
                name: "DkimPublicKey",
                table: "MailDomains");

            migrationBuilder.DropColumn(
                name: "DnsCheckedAt",
                table: "MailDomains");

            migrationBuilder.DropColumn(
                name: "MissingSince",
                table: "MailDomains");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "MailDomains");

            migrationBuilder.DropColumn(
                name: "MissingSince",
                table: "MailAliases");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "MailAliases");

            migrationBuilder.DropColumn(
                name: "MissingSince",
                table: "MailAccounts");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "MailAccounts");

            migrationBuilder.DropColumn(
                name: "UsageMeasuredAt",
                table: "MailAccounts");

            migrationBuilder.DropColumn(
                name: "UsedMb",
                table: "MailAccounts");
        }
    }
}
