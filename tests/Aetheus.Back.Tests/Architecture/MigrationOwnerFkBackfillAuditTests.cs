// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Architectural guard: a migration that gives an <b>existing</b> table a new
/// <b>non-nullable</b> owner column with a sentinel <c>defaultValue: 0</c> and then
/// adds a foreign key for it MUST also seed/backfill the parent rows in the same
/// <c>Up()</c> (via <c>migrationBuilder.Sql(...)</c>) before the FK is created.
///
/// Without the backfill, every existing row carries <c>OrganizationId = 0</c>, no
/// principal row with id 0 exists (identity ids start at 1), and the
/// <c>AddForeignKey</c> fails at runtime with PostgreSQL <c>23503</c> - exactly the
/// production crash that motivated this guard
/// (<c>20260508192206_AddOrganizationOwnership</c>).
///
/// When this test fails, add the seed + backfill inside the migration's
/// <c>Up()</c>, between the column add/alter and the <c>AddForeignKey</c> calls,
/// e.g.:
/// <code>
/// migrationBuilder.Sql("""
///     INSERT INTO "Parents" (...) SELECT ... WHERE NOT EXISTS (...);
///     UPDATE "Children" SET "ParentId" = (...) WHERE "ParentId" = 0;
///     """);
/// </code>
/// </summary>
public class MigrationOwnerFkBackfillAuditTests
{
    [Fact]
    public void EveryNonNullableOwnerFkMigration_SeedsOrBackfillsItsParent()
    {
        var repoRoot = FindRepoRoot();
        var migrationsDir = Path.Combine(repoRoot, "src", "Aetheus.Back", "Data", "Migrations");
        Assert.True(Directory.Exists(migrationsDir), $"Migrations directory not found: {migrationsDir}");

        var offenders = new List<string>();

        foreach (var file in RepositoryScan.EnumerateTopLevel(migrationsDir, "*.cs"))
        {
            var name = Path.GetFileName(file);
            // Skip the designer/snapshot artefacts - only real migration classes have Up()/Down().
            if (name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase)) continue;

            var source = File.ReadAllText(file);
            var up = ExtractUpBody(source);
            if (up is null) continue;

            var addsSentinelOwnerColumn =
                MatchesCall(up, "AddColumn<int>", "nullable: false", "defaultValue: 0") ||
                MatchesCall(up, "AlterColumn<int>", "nullable: false", "defaultValue: 0", "oldNullable: true");

            var addsForeignKey = up.Contains("migrationBuilder.AddForeignKey(", StringComparison.Ordinal);
            var hasBackfillSql = up.Contains("migrationBuilder.Sql(", StringComparison.Ordinal);

            if (addsSentinelOwnerColumn && addsForeignKey && !hasBackfillSql)
            {
                offenders.Add(name);
            }
        }

        Assert.True(offenders.Count == 0,
            "These migrations add a non-nullable owner column (defaultValue: 0) and a foreign key " +
            "without a migrationBuilder.Sql(...) seed/backfill in Up(). They will fail with " +
            $"PostgreSQL 23503 on any non-empty database:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders.Select(o => $"  - {o}")) +
            $"{Environment.NewLine}Seed the parent and backfill the sentinel-0 rows before the AddForeignKey calls.");
    }

    /// <summary>
    /// Returns the text of the migration's <c>Up</c> method (so the <c>Down</c> body, which may
    /// legitimately re-add FKs without a backfill, is never inspected). Null when no Up() exists.
    /// </summary>
    private static string? ExtractUpBody(string source)
    {
        var upIdx = source.IndexOf("void Up(", StringComparison.Ordinal);
        if (upIdx < 0) return null;

        var downIdx = source.IndexOf("void Down(", StringComparison.Ordinal);
        return downIdx > upIdx
            ? source[upIdx..downIdx]
            : source[upIdx..];
    }

    /// <summary>
    /// True when <paramref name="text"/> contains at least one <c>migrationBuilder.{call}( ... );</c>
    /// invocation whose argument list (possibly spanning many lines) contains every required token.
    /// </summary>
    private static bool MatchesCall(string text, string call, params string[] requiredTokens)
    {
        var pattern = "migrationBuilder\\." + Regex.Escape(call) + @"\s*\(.*?\);";
        foreach (Match m in Regex.Matches(text, pattern, RegexOptions.Singleline))
        {
            if (requiredTokens.All(t => m.Value.Contains(t, StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
