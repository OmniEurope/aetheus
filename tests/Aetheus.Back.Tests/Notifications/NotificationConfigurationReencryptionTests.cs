// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Back.Tests.Notifications;

public class NotificationConfigurationReencryptionTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"NotificationReencryptDb_{Guid.NewGuid()}")
            .Options);

    private static EncryptionService NewEncryption()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:EncryptionKey"] = "test-encryption-key-for-notifications!"
            })
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName = Environments.Development;
        return new EncryptionService(config, environment);
    }

    [Fact]
    public async Task RunAsync_EncryptsLegacyPlaintextConfiguration_AndIsIdempotent()
    {
        await using var db = NewDb();
        var encryption = NewEncryption();
        const string plaintext = "{\"webhookUrl\":\"https://hooks.example/secret\"}";
        db.NotificationChannels.Add(new NotificationChannel
        {
            Name = "legacy",
            Type = NotificationChannelType.Slack,
            ConfigurationJson = plaintext
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await NotificationConfigurationReencryption.RunAsync(
            db, encryption, NullLogger.Instance, TestContext.Current.CancellationToken);
        var first = (await db.NotificationChannels.SingleAsync(TestContext.Current.CancellationToken))
            .ConfigurationJson;
        await NotificationConfigurationReencryption.RunAsync(
            db, encryption, NullLogger.Instance, TestContext.Current.CancellationToken);
        var second = (await db.NotificationChannels.SingleAsync(TestContext.Current.CancellationToken))
            .ConfigurationJson;

        Assert.NotEqual(plaintext, first);
        Assert.Equal(plaintext, encryption.DecryptValue(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task RunAsync_LeavesEncryptedConfigurationUntouched()
    {
        await using var db = NewDb();
        var encryption = NewEncryption();
        var encrypted = encryption.EncryptValue("{\"url\":\"https://example.com\"}");
        db.NotificationChannels.Add(new NotificationChannel
        {
            Name = "current",
            Type = NotificationChannelType.Webhook,
            ConfigurationJson = encrypted
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await NotificationConfigurationReencryption.RunAsync(
            db, encryption, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Equal(
            encrypted,
            (await db.NotificationChannels.SingleAsync(TestContext.Current.CancellationToken))
                .ConfigurationJson);
    }
}
