// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Back.Tests.DataSeed;

public sealed class AppDbContextTimestampTests
{
    [Fact]
    public void SaveChanges_SynchronousPath_UsesConfiguredTimeProvider()
    {
        var expected = new DateTimeOffset(2026, 7, 18, 9, 30, 0, TimeSpan.Zero);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(expected));
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organization = new Organization { Name = "Timed", Slug = "timed" };
        db.Organizations.Add(organization);

        db.SaveChanges();

        Assert.Equal(expected.UtcDateTime, organization.CreatedAt);
        Assert.Equal(expected.UtcDateTime, organization.UpdatedAt);
    }
}
