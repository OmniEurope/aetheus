// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Components.Vaults;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.Vaults;

public class SecretExpirationServiceTests
{
    private static (SecretExpirationService Sut, IVaultRepository VaultRepo, INotificationService Notifier) BuildSut(
        IConfiguration config, FakeTimeProvider clock)
    {
        var vaultRepo = Substitute.For<IVaultRepository>();
        var notifier = Substitute.For<INotificationService>();
        var sp = new ServiceCollection()
            .AddScoped(_ => vaultRepo)
            .AddScoped(_ => notifier)
            .BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var logger = Substitute.For<ILogger<SecretExpirationService>>();
        return (new SecretExpirationService(scopeFactory, config, logger, clock), vaultRepo, notifier);
    }

    [Fact]
    public async Task ProcessExpiringSecretsAsync_QueriesWithAlertThresholdAndNotifies()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SecretExpiration:AlertDays"] = "14" })
            .Build();
        var (sut, vaultRepo, notifier) = BuildSut(config, clock);

        var secret = new VaultSecret
        {
            Id = 11,
            Key = "API_TOKEN",
            ExpiresAt = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc),
            Vault = new Vault { Name = "prod" }
        };
        vaultRepo.GetExpiringSecretsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([secret]);

        await sut.ProcessExpiringSecretsAsync(TestContext.Current.CancellationToken);

        // Threshold = now + AlertDays (14) = 2026-06-30
        await vaultRepo.Received(1).GetExpiringSecretsAsync(
            new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>());
        await notifier.Received(1).SendEventAsync("SecretExpiring", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessExpiringSecretsAsync_NoExpiringSecrets_DoesNotNotify()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));
        var (sut, vaultRepo, notifier) = BuildSut(new ConfigurationBuilder().Build(), clock);
        vaultRepo.GetExpiringSecretsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);

        await sut.ProcessExpiringSecretsAsync(TestContext.Current.CancellationToken);

        await notifier.DidNotReceive().SendEventAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
