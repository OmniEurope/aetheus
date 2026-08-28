// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// Blue-green shares one database between both colours, so while the new colour migrates the old one
/// is still answering requests against the same schema. A migration that drops or renames anything
/// therefore takes production down mid-deployment. These tests pin the gate that refuses it, and
/// they exist because an earlier version of the blue-green step dropped this check entirely while
/// still claiming to enforce it.
/// </summary>
public sealed class BlueGreenMigrationGateTests
{
    [Theory]
    [InlineData("DropColumn")]
    [InlineData("DropTable")]
    [InlineData("DropForeignKey")]
    [InlineData("DropIndex")]
    [InlineData("RenameColumn")]
    [InlineData("RenameTable")]
    [InlineData("AlterColumn")]
    public void Up_WithADestructiveOperation_IsRejected(string operation)
    {
        var source = $$"""
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.{{operation}}(name: "Thing", table: "Things");
            }
            """;

        var violations = BlueGreenMigrationGate.Inspect("20260101000000_Test", source);

        Assert.Single(violations);
        Assert.Contains("not expand-compatible", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Up_ThatOnlyAdds_IsAccepted()
    {
        const string source = """
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<int>(name: "Counter", table: "Pipelines", defaultValue: 0);
                migrationBuilder.CreateIndex(name: "IX_Thing", table: "Things", column: "Id");
            }

            protected override void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(name: "Counter", table: "Pipelines");
            }
            """;

        Assert.Empty(BlueGreenMigrationGate.Inspect("20260101000000_Add", source));
    }

    // Down() undoes what Up() added, so dropping there is expected and must not be flagged.
    [Fact]
    public void Down_IsNotInspected()
    {
        const string source = """
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<int>(name: "Counter", table: "Pipelines");
            }

            protected override void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(name: "Pipelines");
                migrationBuilder.RenameColumn(name: "A", table: "B", newName: "C");
            }
            """;

        Assert.Empty(BlueGreenMigrationGate.Inspect("20260101000000_Add", source));
    }

    // Raw SQL is opaque to this analysis, so it is allowed only with an explicit rationale above it.
    [Fact]
    public void RawSql_WithoutARationale_IsRejected()
    {
        const string source = """
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.Sql("UPDATE \"Pipelines\" SET \"BuildCounter\" = 0;");
            }
            """;

        var violations = BlueGreenMigrationGate.Inspect("20260101000000_Sql", source);

        Assert.Contains(violations, v => v.Contains("requires a preceding", StringComparison.Ordinal));
    }

    [Fact]
    public void RawSql_WithARationaleAboveIt_IsAccepted()
    {
        var source = $$"""
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                // {{BlueGreenMigrationGate.SqlReviewMarker}} backfills a new nullable column only.
                migrationBuilder.Sql("UPDATE \"Pipelines\" SET \"BuildCounter\" = 0;");
            }
            """;

        Assert.Empty(BlueGreenMigrationGate.Inspect("20260101000000_Sql", source));
    }

    // A rationale placed after the call proves nothing about the call, so ordering matters.
    [Fact]
    public void RawSql_WithARationaleBelowIt_IsRejected()
    {
        var source = $$"""
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.Sql("DELETE FROM \"Pipelines\";");
                // {{BlueGreenMigrationGate.SqlReviewMarker}} too late to justify the call above.
            }
            """;

        Assert.Contains(
            BlueGreenMigrationGate.Inspect("20260101000000_Sql", source),
            v => v.Contains("requires a preceding", StringComparison.Ordinal));
    }

    // An unreadable Up() means the gate cannot do its job, which must not be silently treated as safe.
    [Fact]
    public void SourceWithoutAnUpMethod_IsRejected()
    {
        var violations = BlueGreenMigrationGate.Inspect("20260101000000_Empty", "public class Nothing { }");

        Assert.Contains(violations, v => v.Contains("no Up(MigrationBuilder)", StringComparison.Ordinal));
    }

    // An expression-bodied Up() carries the operation on the signature line itself. Skipping that line
    // after recognising the signature let the exact migrations this gate exists to stop pass through.
    [Fact]
    public void ExpressionBodiedUp_WithADestructiveOperation_IsRejected()
    {
        const string source = """
            protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "Old", table: "Things");

            protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<int>(name: "Old", table: "Things");
            """;

        var violations = BlueGreenMigrationGate.Inspect("20260101000000_Expression", source);

        Assert.Single(violations);
        Assert.Contains("not expand-compatible", violations[0], StringComparison.Ordinal);
    }

    // NOT NULL with no default is rejected by Postgres on a non-empty table, and the colour still
    // serving inserts rows without the column even on an empty one.
    [Fact]
    public void Up_AddingANotNullColumnWithoutADefault_IsRejected()
    {
        const string source = """
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "Tenant",
                    table: "Pipelines",
                    type: "text",
                    nullable: false);
            }
            """;

        var violations = BlueGreenMigrationGate.Inspect("20260101000000_NotNull", source);

        Assert.Single(violations);
        Assert.Contains("NOT NULL column with no defaultValue", violations[0], StringComparison.Ordinal);
    }

    [Theory]
    // A default makes it satisfiable for rows the previous colour writes…
    [InlineData("nullable: false,\n            defaultValue: \"\"")]
    [InlineData("nullable: false,\n            defaultValueSql: \"now()\"")]
    // …and so does allowing NULL.
    [InlineData("nullable: true")]
    public void Up_AddingASatisfiableColumn_IsAccepted(string arguments)
    {
        var source = $$"""
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(
                    name: "Tenant",
                    table: "Pipelines",
                    {{arguments}});
            }
            """;

        Assert.Empty(BlueGreenMigrationGate.Inspect("20260101000000_NotNull", source));
    }

    [Fact]
    public void Pending_IsWhatTheTreeHasAndTheDatabaseDoesNot()
    {
        string[] discovered = ["20260101000000_A", "20260102000000_B", "20260103000000_C"];
        string[] applied = ["20260101000000_A", "20260102000000_B"];

        Assert.Equal(["20260103000000_C"], BlueGreenMigrationGate.Pending(discovered, applied));
        Assert.Empty(BlueGreenMigrationGate.Pending(discovered, discovered));
        Assert.Equal(discovered, BlueGreenMigrationGate.Pending(discovered, []));
    }

    [Fact]
    public void DiscoverMigrationIds_SkipsDesignerCompanionsAndNonMigrations()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"bg-migrations-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "20260102000000_Second.cs"), "");
            File.WriteAllText(Path.Combine(directory, "20260101000000_First.cs"), "");
            File.WriteAllText(Path.Combine(directory, "20260101000000_First.Designer.cs"), "");
            File.WriteAllText(Path.Combine(directory, "AppDbContextModelSnapshot.cs"), "");

            Assert.Equal(
                ["20260101000000_First", "20260102000000_Second"],
                BlueGreenMigrationGate.DiscoverMigrationIds(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DiscoverMigrationIds_OnAMissingDirectory_IsEmpty() =>
        Assert.Empty(BlueGreenMigrationGate.DiscoverMigrationIds(
            Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}")));
}
