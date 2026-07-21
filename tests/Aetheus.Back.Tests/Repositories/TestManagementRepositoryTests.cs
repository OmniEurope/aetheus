// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.TestManagement;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Repositories;

public class TestManagementRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TestManagementRepository _repo;
    private readonly int _projectId;

    public TestManagementRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new TestManagementRepository(_db);

        var project = new Project { Name = "TestProject" };
        _db.Projects.Add(project);
        _db.SaveChanges();
        _projectId = project.Id;
    }

    [Fact]
    public async Task GetSuitesAsync_ReturnsAllOrdered()
    {
        _db.TestSuites.AddRange(
            new TestSuite { ProjectId = _projectId, Name = "Zeta" },
            new TestSuite { ProjectId = _projectId, Name = "Alpha" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetSuitesAsync(null, ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha", result[0].Name);
    }

    [Fact]
    public async Task GetSuitesAsync_WithProjectId_Filters()
    {
        var p2 = new Project { Name = "P2" };
        _db.Projects.Add(p2);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        _db.TestSuites.AddRange(
            new TestSuite { ProjectId = _projectId, Name = "Suite1" },
            new TestSuite { ProjectId = p2.Id, Name = "Suite2" }
        );
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetSuitesAsync(_projectId, ct: TestContext.Current.CancellationToken);
        Assert.Single(result);
        Assert.Equal("Suite1", result[0].Name);
    }

    [Fact]
    public async Task GetSuiteDetailAsync_Found_IncludesTestCases()
    {
        var suite = new TestSuite { ProjectId = _projectId, Name = "Suite" };
        _db.TestSuites.Add(suite);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.TestCases.Add(new TestCase { TestSuiteId = suite.Id, Name = "Test1" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.GetSuiteDetailAsync(suite.Id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.TestCases);
    }

    [Fact]
    public async Task GetSuiteDetailAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetSuiteDetailAsync(999, ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task FindSuiteAsync_Found()
    {
        var suite = new TestSuite { ProjectId = _projectId, Name = "Suite" };
        _db.TestSuites.Add(suite);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindSuiteAsync(suite.Id, ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindSuiteByNameAsync_Found()
    {
        _db.TestSuites.Add(new TestSuite { ProjectId = _projectId, Name = "Unique" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindSuiteByNameAsync(_projectId, "Unique", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindSuiteByNameAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindSuiteByNameAsync(_projectId, "Missing", ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddSuiteAsync_Persists()
    {
        await _repo.AddSuiteAsync(new TestSuite { ProjectId = _projectId, Name = "New" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.TestSuites.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveSuiteAsync_Removes()
    {
        var suite = new TestSuite { ProjectId = _projectId, Name = "Del" };
        _db.TestSuites.Add(suite);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.RemoveSuiteAsync(suite, ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await _db.TestSuites.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindTestCaseByMethodAsync_Found()
    {
        var suite = new TestSuite { ProjectId = _projectId, Name = "Suite" };
        _db.TestSuites.Add(suite);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        _db.TestCases.Add(new TestCase { TestSuiteId = suite.Id, Name = "Test", AutomatedTestClass = "MyClass", AutomatedTestMethod = "MyMethod" });
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var result = await _repo.FindTestCaseByMethodAsync(suite.Id, "MyClass", "MyMethod", ct: TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindTestCaseByMethodAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.FindTestCaseByMethodAsync(1, "Missing", "Missing", ct: TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task AddTestCaseAsync_Persists()
    {
        var suite = new TestSuite { ProjectId = _projectId, Name = "Suite" };
        _db.TestSuites.Add(suite);
        await _db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);

        await _repo.AddTestCaseAsync(new TestCase { TestSuiteId = suite.Id, Name = "New" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.TestCases.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        _db.TestSuites.Add(new TestSuite { ProjectId = _projectId, Name = "Pending" });
        await _repo.SaveChangesAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await _db.TestSuites.CountAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    public void Dispose() => _db.Dispose();
}
