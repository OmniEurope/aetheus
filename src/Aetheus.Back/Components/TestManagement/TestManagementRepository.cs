// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Components.TestManagement;

public class TestManagementRepository(AppDbContext db) : ITestManagementRepository
{
    public Task<List<TestSuite>> GetSuitesAsync(int? projectId, CancellationToken ct = default)
        => GetSuitesAsync(projectId, null, ct);

    public async Task<List<TestSuite>> GetSuitesAsync(int? projectId, List<int>? accessibleProjectIds, CancellationToken ct = default)
    {
        var query = db.TestSuites
            .AsNoTracking()
            .AsQueryable();

        if (projectId.HasValue)
            query = query.Where(s => s.ProjectId == projectId.Value);

        // F-10: when caller is not Admin (accessibleProjectIds != null), restrict to those projects.
        if (accessibleProjectIds is not null)
            query = query.Where(s => accessibleProjectIds.Contains(s.ProjectId));

        query = query.Include(s => s.Pipeline).Include(s => s.TestCases).AsSplitQuery();

        return await query.OrderBy(s => s.Name).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<TestSuite?> GetSuiteDetailAsync(int id, CancellationToken ct = default)
    {
        return await db.TestSuites
            .AsNoTracking()
            .Include(s => s.Pipeline)
            .Include(s => s.TestCases)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<TestSuite?> FindSuiteAsync(int id, CancellationToken ct = default)
    {
        return await db.TestSuites
            .Include(s => s.TestCases)
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<TestSuite?> FindSuiteByNameAsync(int projectId, string name, CancellationToken ct = default)
    {
        return await db.TestSuites
            .Include(s => s.TestCases)
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Name == name, ct)
            .ConfigureAwait(false);
    }

    public async Task AddSuiteAsync(TestSuite suite, CancellationToken ct = default)
    {
        db.TestSuites.Add(suite);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveSuiteAsync(TestSuite suite, CancellationToken ct = default)
    {
        db.TestSuites.Remove(suite);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<TestCase?> FindTestCaseByMethodAsync(int suiteId, string testClass, string testMethod, CancellationToken ct = default)
    {
        return await db.TestCases
            .FirstOrDefaultAsync(tc => tc.TestSuiteId == suiteId
                && tc.AutomatedTestClass == testClass
                && tc.AutomatedTestMethod == testMethod, ct)
            .ConfigureAwait(false);
    }

    public async Task AddTestCaseAsync(TestCase testCase, CancellationToken ct = default)
    {
        db.TestCases.Add(testCase);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
