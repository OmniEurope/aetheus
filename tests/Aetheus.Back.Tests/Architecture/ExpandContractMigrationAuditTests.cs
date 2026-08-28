// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

public sealed partial class ExpandContractMigrationAuditTests
{
    private const string GuardBaseline = "20260719103123_AddFirewallCollectionDiagnostics.cs";
    private const string SqlReviewMarker = "AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED:";

    [Fact]
    public void FutureMigrationUpMethods_RespectExpandContract()
    {
        var migrations = RepositoryScan.Enumerate(Path.Combine(FindRepoRoot(), "src", "Aetheus.Back", "Data", "Migrations"), "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal)
                           && !path.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal)
                           && string.CompareOrdinal(Path.GetFileName(path), GuardBaseline) > 0)
            .ToList();
        var violations = new List<string>();

        foreach (var path in migrations)
        {
            var source = File.ReadAllText(path);
            var up = ExtractUpMethod(source);
            if (DestructiveOperationRegex().IsMatch(up))
                violations.Add($"{Path.GetFileName(path)} uses a destructive EF operation in Up().");
            var sqlMatch = SqlCallRegex().Match(up);
            if (sqlMatch.Success)
            {
                var markerIndex = up.IndexOf(SqlReviewMarker, StringComparison.Ordinal);
                if (markerIndex < 0 || markerIndex >= sqlMatch.Index)
                    violations.Add($"{Path.GetFileName(path)} uses migrationBuilder.Sql without a preceding reviewed rationale.");
            }
        }

        Assert.True(violations.Count == 0,
            "Future migrations must be expand-only until V+2. " + string.Join(" ", violations));
    }

    private static string ExtractUpMethod(string source)
    {
        var start = source.IndexOf("protected override void Up(MigrationBuilder migrationBuilder)", StringComparison.Ordinal);
        var end = source.IndexOf("protected override void Down(MigrationBuilder migrationBuilder)", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Migration Up()/Down() methods could not be located.");
        return source[start..end];
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;

    [GeneratedRegex(@"migrationBuilder\.(?:Drop(?:Column|Table|ForeignKey|PrimaryKey|UniqueConstraint|CheckConstraint|Index|Sequence)|Rename(?:Column|Table|Index|Sequence)|AlterColumn)(?:<[^>]+>)?\s*\(")]
    private static partial Regex DestructiveOperationRegex();

    [GeneratedRegex(@"migrationBuilder\s*\.\s*Sql\s*\(")]
    private static partial Regex SqlCallRegex();
}
