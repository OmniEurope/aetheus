// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

public sealed class MigrationDesignerCompletenessAuditTests
{
    [Fact]
    public void EveryMigrationHasMatchingDesignerAndFullMigrationId()
    {
        var migrationDirectory = Path.Combine(
            FindRepoRoot(),
            "src",
            "Aetheus.Back",
            "Data",
            "Migrations");
        var migrationFiles = RepositoryScan.EnumerateTopLevel(migrationDirectory, "*.cs")
            .Where(path => Regex.IsMatch(
                Path.GetFileName(path),
                @"^\d{14}_.+(?<!\.Designer)\.cs$",
                RegexOptions.CultureInvariant))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(migrationFiles.Length >= 95, "The migration source scan is unexpectedly small.");
        foreach (var migrationFile in migrationFiles)
        {
            var migrationId = Path.GetFileNameWithoutExtension(migrationFile);
            var designerFile = Path.Combine(migrationDirectory, $"{migrationId}.Designer.cs");
            Assert.True(File.Exists(designerFile), $"Missing Designer for migration {migrationId}.");

            var source = File.ReadAllText(migrationFile);
            var designer = File.ReadAllText(designerFile);
            Assert.Contains("partial class", source, StringComparison.Ordinal);
            Assert.Contains($"[Migration(\"{migrationId}\")]", designer, StringComparison.Ordinal);
            Assert.Contains("BuildTargetModel(ModelBuilder modelBuilder)", designer, StringComparison.Ordinal);
        }
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
