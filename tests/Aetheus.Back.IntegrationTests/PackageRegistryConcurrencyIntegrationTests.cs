// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class PackageRegistryConcurrencyIntegrationTests(PostgresFixture fixture)
    : RelationalTestBase(fixture)
{
    [Fact]
    public async Task ConcurrentDbContexts_CannotPublishDuplicatePackageIdentity()
    {
        await ResetAndMigrateAsync();
        await using var first = NewContext();
        await using var second = NewContext();
        first.RegistryPackages.Add(NewPackage());
        second.RegistryPackages.Add(NewPackage());

        var results = await Task.WhenAll(TrySaveAsync(first), TrySaveAsync(second));

        Assert.Single(results, saved => saved);
        await using var verify = NewContext();
        Assert.Equal(
            1,
            await verify.RegistryPackages.CountAsync(
                package => package.Kind == PackageRegistryKind.NuGet
                    && package.NormalizedName == "aetheus.concurrent",
                TestContext.Current.CancellationToken));
    }

    private static RegistryPackage NewPackage()
    {
        var now = DateTime.UtcNow;
        return new RegistryPackage
        {
            Kind = PackageRegistryKind.NuGet,
            Name = "Aetheus.Concurrent",
            NormalizedName = "aetheus.concurrent",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static async Task<bool> TrySaveAsync(Aetheus.Back.Data.AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}
