// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

public sealed class AlertProvisioningMigrationAuditTests
{
    [Fact]
    public void ProvisioningKeyBackfill_DeduplicatesLegacyRulesBeforeCreatingUniqueIndex()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "Aetheus.Back", "Data", "Migrations",
            "20260717143532_AddAlertRuleProvisioningKey.cs"));

        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY provisioning_key ORDER BY \"Id\")", migration,
            StringComparison.Ordinal);
        Assert.Contains("ranked.candidate_rank = 1", migration, StringComparison.Ordinal);
        Assert.True(
            migration.IndexOf("ranked.candidate_rank = 1", StringComparison.Ordinal)
            < migration.IndexOf("CreateIndex", StringComparison.Ordinal));
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
