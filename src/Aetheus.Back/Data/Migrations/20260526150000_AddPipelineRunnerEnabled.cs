using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aetheus.Back.Data.Migrations;

/// <inheritdoc />
public partial class AddPipelineRunnerEnabled : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Secure-by-default: every existing server starts with the pipeline-runner capability
        // disabled. Operators must explicitly opt-in per server from the UI. This is intentional
        // - pre-existing runs targeting a server will fail with "no candidate" at scheduling
        // time until the runner is re-enabled, surfacing the policy change to the operator
        // instead of silently auto-promoting servers (the dashboard banner planned by item #11
        // will list the affected servers so they can be re-enabled in one click).
        migrationBuilder.AddColumn<bool>(
            name: "PipelineRunnerEnabled",
            table: "Servers",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PipelineRunnerEnabled",
            table: "Servers");
    }
}
