// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Back.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"TestDb_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // UseSetting is visible while Program.cs executes its top-level statements. A later
        // ConfigureAppConfiguration callback cannot satisfy the startup guards in a clean clone.
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Host=test;Database=test;Username=test;Password=test");
        builder.UseSetting("Auth:JwtKey", "aetheus-dev-key-minimum-32-bytes!!");
        builder.UseSetting("Auth:EncryptionKey", "aetheus-encryption-key-32ch!");
        builder.UseSetting("Auth:AdminPassword", "admin");
        builder.UseSetting("RateLimiting:Disabled", "true");
        // Every factory owns an isolated in-memory database, but demo Git repositories live
        // on disk. Disable opt-in startup seeders so parallel controller fixtures never race
        // on the same Toto bare repository; dedicated seeder tests invoke them explicitly.
        builder.UseSetting("Seed:Demo", "false");
        builder.ConfigureServices(services =>
        {
            var toRemove = services.Where(d =>
                d.ServiceType.FullName?.Contains("EntityFramework") == true ||
                d.ServiceType.FullName?.Contains("Npgsql") == true ||
                d.ImplementationType?.FullName?.Contains("EntityFramework") == true ||
                d.ImplementationType?.FullName?.Contains("Npgsql") == true ||
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(AppDbContext))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }

    public static string GenerateTestToken() => TestTokenFactory.AdminToken();
}
