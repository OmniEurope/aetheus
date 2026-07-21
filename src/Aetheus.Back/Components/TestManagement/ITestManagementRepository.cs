// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Data.Entities;

namespace Aetheus.Back.Components.TestManagement;

public interface ITestManagementRepository
{
    Task<List<TestSuite>> GetSuitesAsync(int? projectId, CancellationToken ct = default);

    Task<List<TestSuite>> GetSuitesAsync(int? projectId, List<int>? accessibleProjectIds, CancellationToken ct = default);
    Task<TestSuite?> GetSuiteDetailAsync(int id, CancellationToken ct = default);
    Task<TestSuite?> FindSuiteAsync(int id, CancellationToken ct = default);
    Task<TestSuite?> FindSuiteByNameAsync(int projectId, string name, CancellationToken ct = default);
    Task AddSuiteAsync(TestSuite suite, CancellationToken ct = default);
    Task RemoveSuiteAsync(TestSuite suite, CancellationToken ct = default);
    Task<TestCase?> FindTestCaseByMethodAsync(int suiteId, string testClass, string testMethod, CancellationToken ct = default);
    Task AddTestCaseAsync(TestCase testCase, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
