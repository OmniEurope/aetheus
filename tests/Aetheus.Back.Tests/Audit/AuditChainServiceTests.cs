// SPDX-License-Identifier: EUPL-1.2
using System.Security.Cryptography;
using System.Text;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Data.Entities;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class AuditChainServiceTests
{
    private readonly IAuditRepository _repoMock = Substitute.For<IAuditRepository>();
    private readonly AuditChainService _sut;

    public AuditChainServiceTests()
    {
        _sut = new AuditChainService(_repoMock);
    }

    // --- ComputeHash ---

    [Fact]
    public void ComputeHash_ValidEntry_ReturnsDeterministicSha256()
    {
        var entry = new AuditLog
        {
            Timestamp = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            Username = "admin",
            Action = "Created",
            EntityType = "Server",
            Details = "test details"
        };
        var previousHash = "abc123";

        var result = _sut.ComputeHash(entry, previousHash);

        // Manually compute the expected hash
        var payload = string.Join('|',
            "v2",
            entry.Timestamp.ToString("O"),
            "admin",
            "Created",
            "Server",
            "test details",
            "abc123");
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeHash_NullDetails_TreatedAsEmpty()
    {
        var entry = new AuditLog
        {
            Timestamp = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            Username = "admin",
            Action = "Created",
            EntityType = "Server",
            Details = null
        };

        var result = _sut.ComputeHash(entry, string.Empty);

        Assert.False(string.IsNullOrEmpty(result));
        Assert.Equal(64, result.Length); // SHA256 hex = 64 chars
    }

    [Fact]
    public void ComputeHash_SameInputs_ProducesSameOutput()
    {
        var entry = new AuditLog
        {
            Timestamp = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Username = "user",
            Action = "Updated",
            EntityType = "Pipeline",
            Details = "info"
        };

        var hash1 = _sut.ComputeHash(entry, "prev");
        var hash2 = _sut.ComputeHash(entry, "prev");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentPreviousHash_ProducesDifferentOutput()
    {
        var entry = new AuditLog
        {
            Timestamp = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Username = "user",
            Action = "Updated",
            EntityType = "Pipeline",
            Details = "info"
        };

        var hash1 = _sut.ComputeHash(entry, "prev1");
        var hash2 = _sut.ComputeHash(entry, "prev2");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_NullEntry_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.ComputeHash(null!, "prev"));
    }

    // --- VerifyChainAsync ---

    [Fact]
    public async Task VerifyChainAsync_EmptyChain_ReturnsValid()
    {
        _repoMock.StreamOrderedAsync().Returns(ToAsyncEnumerable<AuditLog>([]));

        var result = await _sut.VerifyChainAsync(ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.TotalEntries);
    }

    [Fact]
    public async Task VerifyChainAsync_ValidChain_ReturnsValid()
    {
        var entry1 = new AuditLog
        {
            Id = 1,
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Username = "admin",
            Action = "Created",
            EntityType = "Server",
            Details = "first",
            PreviousHash = string.Empty
        };
        entry1.Hash = _sut.ComputeHash(entry1, string.Empty);

        var entry2 = new AuditLog
        {
            Id = 2,
            Timestamp = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Username = "admin",
            Action = "Updated",
            EntityType = "Server",
            Details = "second",
            PreviousHash = entry1.Hash
        };
        entry2.Hash = _sut.ComputeHash(entry2, entry1.Hash);

        _repoMock.StreamOrderedAsync().Returns(ToAsyncEnumerable([entry1, entry2]));

        var result = await _sut.VerifyChainAsync(ct: TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.TotalEntries);
    }

    [Fact]
    public async Task VerifyChainAsync_TamperedHash_ReturnsInvalid()
    {
        var entry = new AuditLog
        {
            Id = 1,
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Username = "admin",
            Action = "Created",
            EntityType = "Server",
            Details = "first",
            PreviousHash = string.Empty,
            Hash = "tampered_hash_value"
        };

        _repoMock.StreamOrderedAsync().Returns(ToAsyncEnumerable([entry]));

        var result = await _sut.VerifyChainAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Equal(1, result.TotalEntries);
        Assert.Equal(1, result.FirstInvalidId);
        Assert.Contains("Hash mismatch", result.ErrorMessage);
    }

    [Fact]
    public async Task VerifyChainAsync_BrokenPreviousHash_ReturnsInvalid()
    {
        var entry1 = new AuditLog
        {
            Id = 1,
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Username = "admin",
            Action = "Created",
            EntityType = "Server",
            PreviousHash = string.Empty
        };
        entry1.Hash = _sut.ComputeHash(entry1, string.Empty);

        var entry2 = new AuditLog
        {
            Id = 2,
            Timestamp = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Username = "admin",
            Action = "Updated",
            EntityType = "Server",
            PreviousHash = "wrong_previous_hash"
        };
        entry2.Hash = _sut.ComputeHash(entry2, "wrong_previous_hash");

        _repoMock.StreamOrderedAsync().Returns(ToAsyncEnumerable([entry1, entry2]));

        var result = await _sut.VerifyChainAsync(ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.FirstInvalidId);
        Assert.Contains("PreviousHash mismatch", result.ErrorMessage);
    }

    // --- VerifyUpToEntryAsync ---

    private (AuditLog e1, AuditLog e2) BuildValidPair()
    {
        var e1 = new AuditLog
        {
            Id = 1,
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Username = "admin",
            Action = "Created",
            EntityType = "Server",
            Details = "first",
            PreviousHash = string.Empty
        };
        e1.Hash = _sut.ComputeHash(e1, string.Empty);
        var e2 = new AuditLog
        {
            Id = 2,
            Timestamp = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Username = "admin",
            Action = "Updated",
            EntityType = "Server",
            Details = "second",
            PreviousHash = e1.Hash
        };
        e2.Hash = _sut.ComputeHash(e2, e1.Hash);
        return (e1, e2);
    }

    [Fact]
    public async Task VerifyUpToEntryAsync_ReachesTargetWithIntactChain_ReturnsValid()
    {
        var (e1, e2) = BuildValidPair();
        _repoMock.StreamOrderedAsync().Returns(ToAsyncEnumerable([e1, e2]));

        var result = await _sut.VerifyUpToEntryAsync(1, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result!.IsValid);
        Assert.Equal(1, result.TotalEntries); // stopped at the target, did not walk the whole chain
    }

    [Fact]
    public async Task VerifyUpToEntryAsync_TamperBeforeTarget_ReturnsInvalid()
    {
        var (e1, e2) = BuildValidPair();
        e1.Hash = "tampered"; // corrupt the first link
        _repoMock.StreamOrderedAsync().Returns(ToAsyncEnumerable([e1, e2]));

        var result = await _sut.VerifyUpToEntryAsync(2, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.False(result!.IsValid);
        Assert.Equal(1, result.FirstInvalidId);
    }

    [Fact]
    public async Task VerifyUpToEntryAsync_UnknownId_ReturnsNull()
    {
        var (e1, e2) = BuildValidPair();
        _repoMock.StreamOrderedAsync().Returns(ToAsyncEnumerable([e1, e2]));

        var result = await _sut.VerifyUpToEntryAsync(999, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // Helper to produce IAsyncEnumerable from a list
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(List<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }
}
