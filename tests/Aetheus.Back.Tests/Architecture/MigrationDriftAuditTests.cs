// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Architectural guard: fails the build/test pipeline whenever the EF Core model has drifted
/// from the latest applied migration. Prevents the production failure mode where the runtime
/// queries a column that was never added by a migration (e.g. <c>42703 column does not exist</c>).
///
/// When this test fails, generate the missing migration:
/// <code>
/// cd Aetheus.Back
/// dotnet ef migrations add &lt;Name&gt; --context AppDbContext -o Data/Migrations
/// </code>
/// </summary>
public class MigrationDriftAuditTests
{
    [Fact]
    public void Model_HasNoPendingChanges_AgainstLatestSnapshot()
    {
        // Use Npgsql provider (same as production migrations) with a non-connecting connection
        // string. Model building does not require an actual database connection.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=_drift_check_;Username=_;Password=_")
            .Options;

        using var context = new AppDbContext(options);

        var differ = context.GetService<IMigrationsModelDiffer>();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;

        Assert.NotNull(snapshot);

        // Finalize the snapshot model so its relational data is available for diffing.
        var modelRuntimeInitializer = context.GetService<IModelRuntimeInitializer>();
        var finalizedSnapshotModel = modelRuntimeInitializer.Initialize((IModel)snapshot!.Model);

        var snapshotRelationalModel = finalizedSnapshotModel.GetRelationalModel();
        var currentRelationalModel = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        var differences = differ.GetDifferences(snapshotRelationalModel, currentRelationalModel);

        Assert.True(
            differences.Count == 0,
            $"EF model has {differences.Count} pending change(s) not captured in a migration. " +
            $"Differences: {string.Join(", ", differences.Select(Describe))}. " +
            "Run: cd Aetheus.Back && dotnet ef migrations add <Name> --context AppDbContext -o Data/Migrations");
    }

    private static string Describe(MigrationOperation operation) => operation switch
    {
        AddColumnOperation item => $"{item.GetType().Name}({item.Table}.{item.Name})",
        AlterColumnOperation item => $"{item.GetType().Name}({item.Table}.{item.Name})",
        DropColumnOperation item => $"{item.GetType().Name}({item.Table}.{item.Name})",
        CreateIndexOperation item => $"{item.GetType().Name}({item.Table}.{item.Name})",
        DropIndexOperation item => $"{item.GetType().Name}({item.Table}.{item.Name})",
        _ => operation.GetType().Name
    };
}
