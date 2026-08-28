// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.PackageRegistry;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Aetheus.Back.Tests.PackageRegistry;

/// <summary>
/// The registry repository's read model: kind/name lookups, the listed-and-prerelease gate on
/// search, the paged projection and the tracking split between the read and the update overloads.
///
/// The <c>search</c> argument is deliberately absent from these cases: it reaches
/// <c>EF.Functions.ILike</c>, a PostgreSQL-only translation with no in-memory equivalent, so it is
/// proved against a real database in the integration suite rather than faked here. What IS covered
/// is the blank-search short circuit, which is the branch that decides whether ILike runs at all.
/// </summary>
public sealed class PackageRegistryRepositoryTests : IDisposable
{
    private static readonly DateTime Origin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Origin));
    private readonly AppDbContext _db;
    private readonly PackageRegistryRepository _repository;

    public PackageRegistryRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options, _clock);
        _repository = new PackageRegistryRepository(_db);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SaveAsync() => await _db.SaveChangesAsync(Ct);

    /// <summary>Inserts one row as if the wall clock read <paramref name="instant"/>.</summary>
    private async Task SaveAtAsync(DateTime instant, object entity)
    {
        _clock.SetUtcNow(new DateTimeOffset(instant));
        _db.Add(entity);
        await _db.SaveChangesAsync(Ct);
    }

    private static RegistryPackage Package(
        int id, string name, PackageRegistryKind kind = PackageRegistryKind.NuGet,
        string? description = null, string? distTags = null) =>
        new()
        {
            Id = id,
            Kind = kind,
            Name = name,
            NormalizedName = name.ToLowerInvariant(),
            Description = description,
            DistTagsJson = distTags
        };

    private static RegistryPackageVersion Version(
        int id, int packageId, string version, bool listed = true, bool prerelease = false,
        long sizeBytes = 0, string metadata = "{}", string filePath = "") =>
        new()
        {
            Id = id,
            RegistryPackageId = packageId,
            Version = version,
            NormalizedVersion = version.ToLowerInvariant(),
            IsListed = listed,
            IsPrerelease = prerelease,
            SizeBytes = sizeBytes,
            Metadata = metadata,
            FilePath = filePath
        };

    // ---------- single-package lookups ----------

    [Fact]
    public async Task GetPackageAsync_MatchesOnKindAndNormalizedNameAndLoadsTheVersions()
    {
        _db.RegistryPackages.AddRange(
            Package(1, "Serilog"),
            Package(2, "Serilog", PackageRegistryKind.Npm));
        _db.RegistryPackageVersions.AddRange(
            Version(1, 1, "1.0.0"),
            Version(2, 1, "2.0.0"),
            Version(3, 2, "9.9.9"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var nuget = await _repository.GetPackageAsync(PackageRegistryKind.NuGet, "serilog", Ct);

        Assert.Equal(1, nuget!.Id);
        Assert.Equal(["1.0.0", "2.0.0"], nuget.Versions.Select(version => version.Version).Order());
        Assert.Null(await _repository.GetPackageAsync(PackageRegistryKind.NuGet, "Serilog", Ct));
    }

    [Fact]
    public async Task GetPackageAsync_DoesNotTrackWhatItReturns()
    {
        _db.RegistryPackages.Add(Package(1, "serilog"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        await _repository.GetPackageAsync(PackageRegistryKind.NuGet, "serilog", Ct);

        Assert.Empty(_db.ChangeTracker.Entries<RegistryPackage>());
    }

    [Fact]
    public async Task GetPackageForUpdateAsync_TracksTheEntityItReturns()
    {
        _db.RegistryPackages.Add(Package(1, "serilog"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var package = await _repository.GetPackageForUpdateAsync(PackageRegistryKind.NuGet, "serilog", Ct);
        package!.Description = "structured logging";
        await _repository.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        Assert.Equal(
            "structured logging",
            (await _repository.GetPackageAsync(PackageRegistryKind.NuGet, "serilog", Ct))!.Description);
    }

    [Fact]
    public async Task GetPackageByIdAsync_AndItsUpdateOverload_DifferOnlyInTracking()
    {
        _db.RegistryPackages.Add(Package(1, "serilog"));
        _db.RegistryPackageVersions.Add(Version(1, 1, "1.0.0"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var read = await _repository.GetPackageByIdAsync(1, Ct);
        Assert.Single(read!.Versions);
        Assert.Empty(_db.ChangeTracker.Entries<RegistryPackage>());

        var tracked = await _repository.GetPackageByIdForUpdateAsync(1, Ct);
        Assert.Single(tracked!.Versions);
        Assert.NotEmpty(_db.ChangeTracker.Entries<RegistryPackage>());

        Assert.Null(await _repository.GetPackageByIdAsync(404, Ct));
        Assert.Null(await _repository.GetPackageByIdForUpdateAsync(404, Ct));
    }

    [Fact]
    public async Task GetRegistrationPackageAsync_ProjectsEveryVersionWhenNoneIsRequested()
    {
        _db.RegistryPackages.Add(Package(1, "serilog", description: "logging"));
        _db.RegistryPackageVersions.AddRange(
            Version(1, 1, "1.0.0", metadata: "{\"a\":1}"),
            Version(2, 1, "2.0.0", listed: false, metadata: "{\"b\":2}"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var registration = await _repository.GetRegistrationPackageAsync(
            PackageRegistryKind.NuGet, "serilog", null, Ct);

        Assert.Equal("serilog", registration!.Name);
        Assert.Equal("logging", registration.Description);
        Assert.Equal(["1.0.0", "2.0.0"], registration.Versions.Select(version => version.Version).Order());
        Assert.Contains(registration.Versions, version => !version.IsListed);
        Assert.Contains(registration.Versions, version => version.Metadata == "{\"a\":1}");
    }

    [Fact]
    public async Task GetRegistrationPackageAsync_NarrowsToOneVersionWhenAskedTo()
    {
        _db.RegistryPackages.Add(Package(1, "serilog"));
        _db.RegistryPackageVersions.AddRange(Version(1, 1, "1.0.0"), Version(2, 1, "2.0.0"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var registration = await _repository.GetRegistrationPackageAsync(
            PackageRegistryKind.NuGet, "serilog", "2.0.0", Ct);

        Assert.Equal(["2.0.0"], registration!.Versions.Select(version => version.Version));
    }

    [Fact]
    public async Task GetRegistrationPackageAsync_ReturnsNullForAnUnknownPackage()
    {
        Assert.Null(await _repository.GetRegistrationPackageAsync(
            PackageRegistryKind.NuGet, "absent", null, Ct));
    }

    [Fact]
    public async Task GetVersionNamesAsync_ListsTheVersionsOrReturnsNullForAnUnknownPackage()
    {
        _db.RegistryPackages.Add(Package(1, "serilog"));
        _db.RegistryPackageVersions.AddRange(Version(1, 1, "1.0.0"), Version(2, 1, "2.0.0"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var names = await _repository.GetVersionNamesAsync(PackageRegistryKind.NuGet, "serilog", Ct);

        Assert.Equal(["1.0.0", "2.0.0"], names!.Order());
        Assert.Null(await _repository.GetVersionNamesAsync(PackageRegistryKind.NuGet, "absent", Ct));
    }

    [Fact]
    public async Task GetVersionAsync_WalksTheOwningPackageBeforeMatchingTheVersion()
    {
        _db.RegistryPackages.AddRange(
            Package(1, "serilog"),
            Package(2, "serilog", PackageRegistryKind.Npm));
        _db.RegistryPackageVersions.AddRange(
            Version(1, 1, "1.0.0"),
            Version(2, 2, "1.0.0"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var version = await _repository.GetVersionAsync(
            PackageRegistryKind.Npm, "serilog", "1.0.0", Ct);

        Assert.Equal(2, version!.Id);
        Assert.Null(await _repository.GetVersionAsync(PackageRegistryKind.Npm, "serilog", "2.0.0", Ct));
    }

    // ---------- search ----------

    [Fact]
    public async Task SearchAsync_KeepsOnlyPackagesWithAListedStableVersion()
    {
        _db.RegistryPackages.AddRange(
            Package(1, "kept"),
            Package(2, "unlisted-only"),
            Package(3, "prerelease-only"),
            Package(4, "wrong-kind", PackageRegistryKind.Npm));
        _db.RegistryPackageVersions.AddRange(
            Version(1, 1, "1.0.0"),
            Version(2, 2, "1.0.0", listed: false),
            Version(3, 3, "1.0.0-beta", prerelease: true),
            Version(4, 4, "1.0.0"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.SearchAsync(
            PackageRegistryKind.NuGet, null, 0, 50, includePrerelease: false, Ct);

        Assert.Equal(1, total);
        Assert.Equal(["kept"], items.Select(item => item.Name));
    }

    [Fact]
    public async Task SearchAsync_AdmitsPrereleaseOnlyPackagesWhenAskedTo()
    {
        _db.RegistryPackages.Add(Package(1, "prerelease-only"));
        _db.RegistryPackageVersions.Add(Version(1, 1, "1.0.0-beta", prerelease: true));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.SearchAsync(
            PackageRegistryKind.NuGet, "   ", 0, 50, includePrerelease: true, Ct);

        Assert.Equal(["prerelease-only"], items.Select(item => item.Name));
    }

    [Fact]
    public async Task SearchAsync_OrdersByNameAndAppliesTheSkipTakeWindow()
    {
        for (var index = 1; index <= 5; index++)
        {
            _db.RegistryPackages.Add(Package(index, $"pkg-{index:D2}"));
            _db.RegistryPackageVersions.Add(Version(index, index, "1.0.0"));
        }
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.SearchAsync(
            PackageRegistryKind.NuGet, null, 2, 2, includePrerelease: false, Ct);

        Assert.Equal(5, total);
        Assert.Equal(["pkg-03", "pkg-04"], items.Select(item => item.Name));
    }

    [Fact]
    public async Task SearchAsync_ClampsAnOutOfRangeWindowInsteadOfThrowing()
    {
        for (var index = 1; index <= 3; index++)
        {
            _db.RegistryPackages.Add(Package(index, $"pkg-{index:D2}"));
            _db.RegistryPackageVersions.Add(Version(index, index, "1.0.0"));
        }
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.SearchAsync(
            PackageRegistryKind.NuGet, null, -10, 0, includePrerelease: false, Ct);

        Assert.Equal(3, total);
        Assert.Equal(["pkg-01"], items.Select(item => item.Name));
    }

    [Fact]
    public async Task SearchAsync_ProjectsEveryVersionOfAMatchedPackageIncludingTheHiddenOnes()
    {
        _db.RegistryPackages.Add(Package(1, "serilog", description: "logging", distTags: "{\"latest\":\"2.0.0\"}"));
        _db.RegistryPackageVersions.AddRange(
            Version(1, 1, "1.0.0"),
            Version(2, 1, "2.0.0-beta", prerelease: true, listed: false));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.SearchAsync(
            PackageRegistryKind.NuGet, null, 0, 50, includePrerelease: false, Ct);

        var package = Assert.Single(items);
        Assert.Equal("logging", package.Description);
        Assert.Equal("{\"latest\":\"2.0.0\"}", package.DistTagsJson);
        Assert.Equal(2, package.Versions.Count);
        Assert.Contains(package.Versions, version => version is { IsPrerelease: true, IsListed: false });
    }

    // ---------- metadata and paging ----------

    [Fact]
    public async Task GetVersionMetadataAsync_ShortCircuitsOnAnEmptyRequest()
    {
        _db.RegistryPackages.Add(Package(1, "serilog"));
        _db.RegistryPackageVersions.Add(Version(1, 1, "1.0.0", metadata: "{\"a\":1}"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        Assert.Empty(await _repository.GetVersionMetadataAsync([], Ct));
    }

    [Fact]
    public async Task GetVersionMetadataAsync_ReturnsTheMetadataOfTheRequestedVersionsOnly()
    {
        _db.RegistryPackages.Add(Package(1, "serilog"));
        _db.RegistryPackageVersions.AddRange(
            Version(1, 1, "1.0.0", metadata: "{\"a\":1}"),
            Version(2, 1, "2.0.0", metadata: "{\"b\":2}"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var metadata = await _repository.GetVersionMetadataAsync([2, 404], Ct);

        Assert.Equal("{\"b\":2}", Assert.Contains(2, metadata));
        Assert.DoesNotContain(404, metadata);
    }

    [Fact]
    public async Task GetPagedAsync_OrdersByKindThenNameAndPagesTheResult()
    {
        _db.RegistryPackages.AddRange(
            Package(1, "zulu"),
            Package(2, "alpha"),
            Package(3, "npm-alpha", PackageRegistryKind.Npm));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var first = await _repository.GetPagedAsync(null, null, 1, 2, Ct);
        var second = await _repository.GetPagedAsync(null, null, 2, 2, Ct);

        Assert.Equal(3, first.Total);
        Assert.Equal(
            [PackageRegistryKind.NuGet, PackageRegistryKind.NuGet],
            first.Items.Select(item => item.Kind));
        Assert.Equal(["alpha", "zulu"], first.Items.Select(item => item.Name));
        Assert.Equal(["npm-alpha"], second.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task GetPagedAsync_NarrowsToOneKindWhenAskedTo()
    {
        _db.RegistryPackages.AddRange(
            Package(1, "nuget-one"),
            Package(2, "npm-one", PackageRegistryKind.Npm));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, total) = await _repository.GetPagedAsync(PackageRegistryKind.Npm, null, 1, 10, Ct);

        Assert.Equal(1, total);
        Assert.Equal(["npm-one"], items.Select(item => item.Name));
    }

    [Fact]
    public async Task GetPagedAsync_SummarizesTheNewestListedVersionTheCountAndTheTotalSize()
    {
        _db.RegistryPackages.Add(Package(1, "serilog"));
        await SaveAsync();
        // The fake clock only moves forward, so the rows are inserted oldest-first. The newest row
        // is the unlisted one on purpose: the summary must skip it and report 2.0.0.
        await SaveAtAsync(Origin, Version(1, 1, "1.0.0", sizeBytes: 100));
        await SaveAtAsync(Origin.AddMinutes(30), Version(3, 1, "2.0.0", sizeBytes: 300));
        await SaveAtAsync(Origin.AddHours(1), Version(2, 1, "3.0.0", listed: false, sizeBytes: 20));
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.GetPagedAsync(null, null, 1, 10, Ct);

        var summary = Assert.Single(items);
        Assert.Equal("2.0.0", summary.LatestVersion);
        Assert.Equal(3, summary.VersionCount);
        Assert.Equal(420, summary.TotalSizeBytes);
        Assert.Equal(Origin, summary.UpdatedAt);
    }

    [Fact]
    public async Task GetPagedAsync_LeavesTheLatestVersionNullWhenEveryVersionIsUnlisted()
    {
        _db.RegistryPackages.Add(Package(1, "serilog"));
        _db.RegistryPackageVersions.Add(Version(1, 1, "1.0.0", listed: false));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var (items, _) = await _repository.GetPagedAsync(null, null, 1, 10, Ct);

        Assert.Null(Assert.Single(items).LatestVersion);
    }

    // ---------- writes ----------

    [Fact]
    public async Task AddPackageAsync_OnlyStagesTheInsertUntilSaveChangesIsCalled()
    {
        await _repository.AddPackageAsync(Package(1, "serilog"), Ct);

        Assert.Null(await _repository.GetPackageByIdAsync(1, Ct));

        await _repository.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();

        Assert.NotNull(await _repository.GetPackageByIdAsync(1, Ct));
    }

    [Fact]
    public async Task GetStoredFilePathsAsync_ReturnsACaseInsensitiveSetOfEveryStoredPath()
    {
        _db.RegistryPackages.Add(Package(1, "serilog"));
        _db.RegistryPackageVersions.AddRange(
            Version(1, 1, "1.0.0", filePath: "nuget/serilog/1.0.0.nupkg"),
            Version(2, 1, "2.0.0", filePath: "nuget/serilog/2.0.0.nupkg"));
        await SaveAsync();
        _db.ChangeTracker.Clear();

        var paths = await _repository.GetStoredFilePathsAsync(Ct);

        Assert.Equal(2, paths.Count);
        Assert.Contains("NuGet/Serilog/1.0.0.NUPKG", paths);
    }

    public void Dispose() => _db.Dispose();
}
