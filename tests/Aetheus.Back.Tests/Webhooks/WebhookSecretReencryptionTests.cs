// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Webhooks;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class WebhookSecretReencryptionTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ReencryptDb_{Guid.NewGuid()}")
            .Options);

    private static EncryptionService NewEncryption(bool allowLegacyCbc = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:EncryptionKey"] = "test-encryption-key-for-tests!",
                ["Auth:AllowLegacyCbc"] = allowLegacyCbc ? "true" : "false"
            })
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = Environments.Development;
        return new EncryptionService(config, env);
    }

    [Fact]
    public async Task RunAsync_ConvertsResidualPlaintextSecretToCiphertext()
    {
        await using var db = NewDb();
        var encryption = NewEncryption();
        db.WebhookSubscriptions.Add(new WebhookSubscription
        {
            EventType = "pipeline.completed",
            TargetUrl = "https://example.com/hook",
            Secret = "plaintext-secret"
        });
        await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await WebhookSecretReencryption.RunAsync(db, encryption, NullLogger.Instance, ct: TestContext.Current.CancellationToken);

        var stored = (await db.WebhookSubscriptions.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).Secret!;
        Assert.NotEqual("plaintext-secret", stored);
        Assert.Equal("plaintext-secret", encryption.DecryptValue(stored));
    }

    [Fact]
    public async Task RunAsync_LeavesAlreadyEncryptedSecretUntouched()
    {
        await using var db = NewDb();
        var encryption = NewEncryption();
        var cipher = encryption.EncryptValue("already-encrypted");
        db.WebhookSubscriptions.Add(new WebhookSubscription
        {
            EventType = "server.offline",
            TargetUrl = "https://example.com",
            Secret = cipher
        });
        await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await WebhookSecretReencryption.RunAsync(db, encryption, NullLogger.Instance, ct: TestContext.Current.CancellationToken);

        var stored = (await db.WebhookSubscriptions.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).Secret!;
        Assert.Equal(cipher, stored);
        Assert.Equal("already-encrypted", encryption.DecryptValue(stored));
    }

    [Fact]
    public async Task RunAsync_WithLegacyCbcAllowed_StillReencryptsValidBase64Plaintext()
    {
        // #9: with the Auth:AllowLegacyCbc migration switch enabled, a residual plaintext that is valid base64 of
        // ciphertext-like length must NOT be mis-classified as already-encrypted - the version-byte gate
        // (not a trial decryption) forces it through re-encryption.
        await using var db = NewDb();
        var encryption = NewEncryption(allowLegacyCbc: true);
        var plaintext = string.Concat("QUJDREVGR0hJSktMTU5PUFFS", "U1RVVldYWVowMTIzNDU2Nzg5"); // valid base64, not a GCM payload
        db.WebhookSubscriptions.Add(new WebhookSubscription
        {
            EventType = "pipeline.completed",
            TargetUrl = "https://example.com/hook",
            Secret = plaintext
        });
        await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await WebhookSecretReencryption.RunAsync(db, encryption, NullLogger.Instance, ct: TestContext.Current.CancellationToken);

        var stored = (await db.WebhookSubscriptions.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).Secret!;
        Assert.NotEqual(plaintext, stored);
        Assert.Equal(plaintext, encryption.DecryptValue(stored));
    }

    [Fact]
    public async Task RunAsync_IsIdempotent_SecondRunIsANoop()
    {
        await using var db = NewDb();
        var encryption = NewEncryption();
        db.WebhookSubscriptions.Add(new WebhookSubscription
        {
            EventType = "pipeline.completed",
            TargetUrl = "https://example.com/hook",
            Secret = "plaintext-secret"
        });
        await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await WebhookSecretReencryption.RunAsync(db, encryption, NullLogger.Instance, ct: TestContext.Current.CancellationToken);
        var afterFirst = (await db.WebhookSubscriptions.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).Secret!;
        await WebhookSecretReencryption.RunAsync(db, encryption, NullLogger.Instance, ct: TestContext.Current.CancellationToken);
        var afterSecond = (await db.WebhookSubscriptions.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).Secret!;

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal("plaintext-secret", encryption.DecryptValue(afterSecond));
    }
}
