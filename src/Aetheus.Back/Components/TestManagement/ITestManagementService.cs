// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Components.TestManagement;

public interface ITestManagementService
{
    Task<List<TestSuiteDto>> GetSuitesAsync(int? projectId = null, CancellationToken ct = default);

    Task<List<TestSuiteDto>> GetSuitesAsync(int? projectId = null, List<int>? accessibleProjectIds = null, CancellationToken ct = default);
    Task<TestSuiteDetailDto?> GetSuiteDetailAsync(int id, CancellationToken ct = default);
    Task<TestSuiteDto> CreateSuiteAsync(CreateTestSuiteRequest request, CancellationToken ct = default);
    Task<TestSuiteDto?> UpdateSuiteAsync(int id, UpdateTestSuiteRequest request, CancellationToken ct = default);
    Task<bool> DeleteSuiteAsync(int id, CancellationToken ct = default);
    Task<TestIngestionResultDto> IngestTestResultsAsync(IngestTestResultsRequest request, CancellationToken ct = default);
}
