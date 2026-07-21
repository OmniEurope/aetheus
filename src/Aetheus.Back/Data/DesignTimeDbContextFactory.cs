// SPDX-License-Identifier: EUPL-1.2
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Aetheus.Back.Data;

/// <summary>
/// Used by the EF Core CLI (`dotnet ef migrations`) only. Resolves the connection string from
/// the standard ASP.NET Core configuration sources (env vars, appsettings.Development.json, user-secrets)
/// to avoid hard-coding credentials in source.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .Build();

        var connectionString = ResolveConnectionString(configuration);

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new AppDbContext(optionsBuilder.Options);
    }

    internal static string ResolveConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = configuration.GetConnectionString("Default")
            ?? configuration["AETHEUS_DESIGN_CONNECTION"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Design-time database connection is not configured. Set ConnectionStrings:Default " +
                "or AETHEUS_DESIGN_CONNECTION before running dotnet ef.");
        return connectionString;
    }
}
