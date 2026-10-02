// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.VariableLibraries;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class VariableLibraryRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly VariableLibraryRepository _repo;
    private readonly int _projectId;

    public VariableLibraryRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new VariableLibraryRepository(_db);

        var project = new Project { Name = "P" };
        _db.Projects.Add(project);
        _db.SaveChanges();
        _projectId = project.Id;
    }

    [Fact]
    public async Task GetLibrariesPagedAsync_ReturnsPagedOrderedByName()
    {
        _db.VariableLibraries.AddRange(
            new VariableLibrary { Name = "Zeta", Description = "d", ProjectId = _projectId },
            new VariableLibrary { Name = "Alpha", Description = "d", ProjectId = _projectId },
            new VariableLibrary { Name = "Mid", Description = "d", ProjectId = _projectId }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetLibrariesPagedAsync(null, null, null, null, 1, 2, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, total);
        Assert.Equal(2, items.Count);
        Assert.Equal("Alpha", items[0].Name);
    }

    [Fact]
    public async Task GetLibrariesPagedAsync_WithSearch_FiltersNameAndDescription()
    {
        _db.VariableLibraries.AddRange(
            new VariableLibrary { Name = "Prod", Description = "Production vars" },
            new VariableLibrary { Name = "Dev", Description = "Development" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetLibrariesPagedAsync("Prod", null, null, null, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetLibrariesPagedAsync_WithProjectId_IncludesGlobalAndProject()
    {
        _db.VariableLibraries.AddRange(
            new VariableLibrary { Name = "Proj", Description = "d", ProjectId = _projectId },
            new VariableLibrary { Name = "Global", Description = "d", ProjectId = null },
            new VariableLibrary { Name = "Other", Description = "d", ProjectId = _projectId + 100 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (items, total) = await _repo.GetLibrariesPagedAsync(null, _projectId, null, null, 1, 10, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task GetLibrariesPagedAsync_SortByProjectName_OrdersByOwningProject()
    {
        var alpha = new Project { Name = "Alpha" };
        _db.Projects.Add(alpha);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.VariableLibraries.AddRange(
            new VariableLibrary { Name = "A", Description = "d", ProjectId = _projectId },   // project "P"
            new VariableLibrary { Name = "B", Description = "d", ProjectId = alpha.Id }      // project "Alpha"
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var (ascending, _) = await _repo.GetLibrariesPagedAsync(
            null, null, null, null, 1, 10, ct: TestContext.Current.CancellationToken, sortBy: "ProjectName");
        Assert.Equal(["B", "A"], ascending.Select(vl => vl.Name));

        var (descending, _) = await _repo.GetLibrariesPagedAsync(
            null, null, null, null, 1, 10, ct: TestContext.Current.CancellationToken, sortBy: "ProjectName", sortDescending: true);
        Assert.Equal(["A", "B"], descending.Select(vl => vl.Name));
    }

    [Fact]
    public async Task GetLibrariesPagedAsync_WithAccessibleIds_Filters()
    {
        _db.VariableLibraries.AddRange(
            new VariableLibrary { Name = "VL1", Description = "d" },
            new VariableLibrary { Name = "VL2", Description = "d" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var ids = await _db.VariableLibraries.Select(vl => vl.Id).Take(1).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var (items, total) = await _repo.GetLibrariesPagedAsync(null, null, null, null, 1, 10, ids, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetLibraryDetailAsync_Found_DoesNotLoadUnboundedEntries()
    {
        var lib = new VariableLibrary { Name = "VL", Description = "d", ProjectId = _projectId };
        _db.VariableLibraries.Add(lib);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var entry = new VariableLibraryEntry { VariableLibraryId = lib.Id, Key = "K1", Value = "val" };
        _db.VariableLibraryEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.VariableLibraryEntryVersions.Add(new VariableLibraryEntryVersion { VariableLibraryEntryId = entry.Id, Version = 1, Value = "v1" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.ChangeTracker.Clear();

        var result = await _repo.GetLibraryDetailAsync(lib.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Empty(result.Entries);
        Assert.Equal(1, await _repo.GetEntryCountAsync(lib.Id, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetLibraryDetailAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetLibraryDetailAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindByNamesAsync_ReturnsMatchingLibraries()
    {
        _db.VariableLibraries.AddRange(
            new VariableLibrary { Name = "VL1", Description = "d", ProjectId = _projectId },
            new VariableLibrary { Name = "VL2", Description = "d", ProjectId = _projectId },
            new VariableLibrary { Name = "VL3", Description = "d", ProjectId = _projectId + 100 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindByNamesAsync(["VL1", "VL2"], _projectId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetLibraryNamesAsync_ReturnsOrderedNamesForProjectAndGlobal()
    {
        _db.VariableLibraries.AddRange(
            new VariableLibrary { Name = "Zeta", Description = "d", ProjectId = _projectId },
            new VariableLibrary { Name = "Alpha", Description = "d", ProjectId = null },
            new VariableLibrary { Name = "Other", Description = "d", ProjectId = _projectId + 100 }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetLibraryNamesAsync(_projectId, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0]);
    }

    [Fact]
    public async Task GetSuggestionKeysPagedAsync_AggregatesDistinctAccessibleKeys()
    {
        var allowed = new VariableLibrary { Name = "Allowed", Description = "d", ProjectId = _projectId };
        var denied = new VariableLibrary { Name = "Denied", Description = "d", ProjectId = _projectId };
        _db.VariableLibraries.AddRange(allowed, denied);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.VariableLibraryEntries.AddRange(
            new VariableLibraryEntry { VariableLibraryId = allowed.Id, Key = "ALPHA", Value = "1" },
            new VariableLibraryEntry { VariableLibraryId = allowed.Id, Key = "BETA", Value = "2" },
            new VariableLibraryEntry { VariableLibraryId = allowed.Id, Key = "BETA", Value = "3" },
            new VariableLibraryEntry { VariableLibraryId = denied.Id, Key = "SECRET", Value = "4" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetSuggestionKeysPagedAsync(
            _projectId, null, 2, 1, false, [allowed.Id], ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(["BETA"], result.Items);
    }

    [Fact]
    public async Task GetEntriesPagedAsync_ReturnsRequestedPageWithVersionCounts()
    {
        var library = new VariableLibrary { Name = "Paged", Description = "d" };
        _db.VariableLibraries.Add(library);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var entries = Enumerable.Range(1, 30).Select(index => new VariableLibraryEntry
        {
            VariableLibraryId = library.Id,
            Key = $"KEY-{index:00}",
            Value = index.ToString()
        }).ToList();
        _db.VariableLibraryEntries.AddRange(entries);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.VariableLibraryEntryVersions.Add(new VariableLibraryEntryVersion
        {
            VariableLibraryEntryId = entries[25].Id,
            Key = entries[25].Key,
            Value = entries[25].Value,
            Version = 1
        });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetEntriesPagedAsync(
            library.Id, null, 2, 25, "Key", false, ct: TestContext.Current.CancellationToken);

        Assert.Equal(30, result.TotalCount);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal("KEY-26", result.Items[0].Key);
        Assert.Single(result.Items[0].Versions);
    }

    [Fact]
    public async Task FindLibraryAsync_Found()
    {
        var lib = new VariableLibrary { Name = "VL", Description = "d" };
        _db.VariableLibraries.Add(lib);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindLibraryAsync(lib.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddLibraryAsync_Persists()
    {
        await _repo.AddLibraryAsync(new VariableLibrary { Name = "New", Description = "d" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.VariableLibraries.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveLibraryAsync_Removes()
    {
        var lib = new VariableLibrary { Name = "Del", Description = "d" };
        _db.VariableLibraries.Add(lib);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveLibraryAsync(lib, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.VariableLibraries.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindEntryAsync_Found()
    {
        var lib = new VariableLibrary { Name = "VL", Description = "d" };
        _db.VariableLibraries.Add(lib);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var entry = new VariableLibraryEntry { VariableLibraryId = lib.Id, Key = "K", Value = "v" };
        _db.VariableLibraryEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindEntryAsync(entry.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task AddEntryAsync_Persists()
    {
        var lib = new VariableLibrary { Name = "VL", Description = "d" };
        _db.VariableLibraries.Add(lib);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.AddEntryAsync(new VariableLibraryEntry { VariableLibraryId = lib.Id, Key = "K", Value = "v" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.VariableLibraryEntries.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveEntryAsync_Removes()
    {
        var lib = new VariableLibrary { Name = "VL", Description = "d" };
        _db.VariableLibraries.Add(lib);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var entry = new VariableLibraryEntry { VariableLibraryId = lib.Id, Key = "K", Value = "v" };
        _db.VariableLibraryEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveEntryAsync(entry, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.VariableLibraryEntries.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddEntryVersionAsync_Persists()
    {
        var lib = new VariableLibrary { Name = "VL", Description = "d" };
        _db.VariableLibraries.Add(lib);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var entry = new VariableLibraryEntry { VariableLibraryId = lib.Id, Key = "K", Value = "v" };
        _db.VariableLibraryEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.AddEntryVersionAsync(new VariableLibraryEntryVersion { VariableLibraryEntryId = entry.Id, Version = 1, Value = "v1" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.VariableLibraryEntryVersions.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetEntryVersionsAsync_ReturnsOrderedByVersionDesc()
    {
        var lib = new VariableLibrary { Name = "VL", Description = "d" };
        _db.VariableLibraries.Add(lib);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var entry = new VariableLibraryEntry { VariableLibraryId = lib.Id, Key = "K", Value = "v" };
        _db.VariableLibraryEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.VariableLibraryEntryVersions.AddRange(
            new VariableLibraryEntryVersion { VariableLibraryEntryId = entry.Id, Version = 1, Value = "v1" },
            new VariableLibraryEntryVersion { VariableLibraryEntryId = entry.Id, Version = 3, Value = "v3" },
            new VariableLibraryEntryVersion { VariableLibraryEntryId = entry.Id, Version = 2, Value = "v2" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetEntryVersionsPagedAsync(entry.Id, null, 1, 2, null, false, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(3, result.Items[0].Version);
    }

    [Fact]
    public async Task GetNextVersionAsync_ReturnsNextVersion()
    {
        var lib = new VariableLibrary { Name = "VL", Description = "d" };
        _db.VariableLibraries.Add(lib);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var entry = new VariableLibraryEntry { VariableLibraryId = lib.Id, Key = "K", Value = "v" };
        _db.VariableLibraryEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.VariableLibraryEntryVersions.Add(new VariableLibraryEntryVersion { VariableLibraryEntryId = entry.Id, Version = 5, Value = "v" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetNextVersionAsync(entry.Id, ct: TestContext.Current.CancellationToken);
        Assert.Equal(6, result);
    }

    [Fact]
    public async Task GetNextVersionAsync_NoVersions_Returns1()
    {
        var lib = new VariableLibrary { Name = "VL", Description = "d" };
        _db.VariableLibraries.Add(lib);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var entry = new VariableLibraryEntry { VariableLibraryId = lib.Id, Key = "K", Value = "v" };
        _db.VariableLibraryEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetNextVersionAsync(entry.Id, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.VariableLibraries.Add(new VariableLibrary { Name = "P", Description = "d" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.VariableLibraries.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
