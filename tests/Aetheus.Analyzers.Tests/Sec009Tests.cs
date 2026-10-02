// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Analyzers.Tests;

/// <summary>
/// SEC009 reports MD5, SHA-1 and System.Random only where the names around them say a secret is
/// being made or protected. The same primitives used for Git object ids, cache keys or jitter must
/// stay silent, or the rule would be switched off within a week.
/// </summary>
public sealed class Sec009Tests
{
    private static Task VerifyAsync(string source) =>
        MarkupVerifier<SEC009_NoWeakPrimitiveForASecretAnalyzer>.VerifyAsync(source);

    [Fact]
    public async Task AWeakPrimitiveProducingASecretIsReported()
    {
        await VerifyAsync("""
            using System;
            using System.Security.Cryptography;
            using System.Text;
            public sealed class Accounts
            {
                private string _apiKey = "";

                public string IssueResetToken()
                {
                    var resetToken = {|SEC009:Random.Shared|}.Next().ToString();
                    return resetToken;
                }

                public string HashPassword(string value) =>
                    Convert.ToHexString({|SEC009:SHA1.HashData(Encoding.UTF8.GetBytes(value))|});

                public void Rotate()
                {
                    using var passwordHasher = {|SEC009:MD5.Create()|};
                    _apiKey = {|SEC009:new Random()|}.Next().ToString();
                }

                public void Seed(byte[] buffer) => Fill(salt: {|SEC009:new Random(42)|});
                private static void Fill(Random salt) { }
            }
            """);
    }

    [Fact]
    public async Task TheSamePrimitivesForNonSecretPurposesAreNotReported()
    {
        await VerifyAsync("""
            using System;
            using System.Security.Cryptography;
            using System.Threading;
            using System.Threading.Tasks;
            public sealed class Plumbing
            {
                public byte[] ComputeObjectId(byte[] content) => SHA1.HashData(content);

                public string CacheKeyFor(byte[] content)
                {
                    var cacheKey = Convert.ToHexString(MD5.HashData(content));
                    return cacheKey;
                }

                public Task BackOffAsync(CancellationToken cancellationToken) =>
                    Task.Delay(Random.Shared.Next(100, 500), cancellationToken);
            }
            """);
    }

    [Fact]
    public async Task KnownFalsePositive_JitterInsideAMethodNamedAfterASecretIsReported()
    {
        // Documented false positive: Random is the right tool for retry jitter, but the enclosing
        // method is named after a token, and the enclosing name is part of the context read.
        await VerifyAsync("""
            using System;
            using System.Threading.Tasks;
            public sealed class Session
            {
                public Task RefreshTokenAsync() => Task.Delay({|SEC009:Random.Shared|}.Next(100, 500));
            }
            """);
    }
}
