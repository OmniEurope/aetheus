// SPDX-License-Identifier: EUPL-1.2
using System.Xml.Linq;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Components.TestManagement;

public class TestManagementService(ITestManagementRepository repo, IAuditService audit, TimeProvider timeProvider) : ITestManagementService
{
    public Task<List<TestSuiteDto>> GetSuitesAsync(int? projectId = null, CancellationToken ct = default)
        => GetSuitesAsync(projectId, null, ct);

    public async Task<List<TestSuiteDto>> GetSuitesAsync(int? projectId = null, List<int>? accessibleProjectIds = null, CancellationToken ct = default)
    {
        var suites = await repo.GetSuitesAsync(projectId, accessibleProjectIds, ct).ConfigureAwait(false);
        return suites.Select(MapToDto).ToList();
    }

    public async Task<TestSuiteDetailDto?> GetSuiteDetailAsync(int id, CancellationToken ct = default)
    {
        var suite = await repo.GetSuiteDetailAsync(id, ct).ConfigureAwait(false);
        if (suite is null) return null;

        return new TestSuiteDetailDto
        {
            Id = suite.Id,
            ProjectId = suite.ProjectId,
            Name = suite.Name,
            Description = suite.Description,
            Type = suite.Type,
            PipelineId = suite.PipelineId,
            PipelineName = suite.Pipeline?.Name,
            TestCases = suite.TestCases.Select(MapTestCaseDto).ToList(),
            CreatedAt = suite.CreatedAt
        };
    }

    public async Task<TestSuiteDto> CreateSuiteAsync(CreateTestSuiteRequest request, CancellationToken ct = default)
    {
        var entity = new TestSuite
        {
            ProjectId = request.ProjectId,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            PipelineId = request.PipelineId
        };

        await repo.AddSuiteAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Created", "TestSuite", entity.Id, request.Name, ct).ConfigureAwait(false);
        return MapToDto(entity);
    }

    public async Task<TestSuiteDto?> UpdateSuiteAsync(int id, UpdateTestSuiteRequest request, CancellationToken ct = default)
    {
        var entity = await repo.FindSuiteAsync(id, ct).ConfigureAwait(false);
        if (entity is null) return null;

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.Type = request.Type;
        entity.PipelineId = request.PipelineId;
        entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await audit.LogAsync("Updated", "TestSuite", id, null, ct).ConfigureAwait(false);
        return MapToDto(entity);
    }

    public async Task<bool> DeleteSuiteAsync(int id, CancellationToken ct = default)
    {
        var entity = await repo.FindSuiteAsync(id, ct).ConfigureAwait(false);
        if (entity is null) return false;

        await repo.RemoveSuiteAsync(entity, ct).ConfigureAwait(false);
        await audit.LogAsync("Deleted", "TestSuite", id, null, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<TestIngestionResultDto> IngestTestResultsAsync(IngestTestResultsRequest request, CancellationToken ct = default)
    {
        var suite = await repo.FindSuiteByNameAsync(request.ProjectId, request.SuiteName, ct).ConfigureAwait(false);
        var isNew = suite is null;

        suite ??= new TestSuite
        {
            ProjectId = request.ProjectId,
            Name = request.SuiteName,
            Type = TestSuiteType.Automated
        };

        var testCases = ParseTestResults(request.XmlContent, request.Format);
        int newCount = 0, updatedCount = 0;

        foreach (var parsed in testCases)
        {
            var existing = suite.TestCases
                .FirstOrDefault(tc => tc.AutomatedTestClass == parsed.ClassName && tc.AutomatedTestMethod == parsed.MethodName);

            if (existing is not null)
            {
                existing.LastOutcome = parsed.Outcome;
                existing.LastDurationMs = parsed.DurationMs;
                existing.LastPipelineRunId = request.PipelineRunId;
                existing.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
                updatedCount++;
            }
            else
            {
                suite.TestCases.Add(new TestCase
                {
                    Name = $"{parsed.ClassName}.{parsed.MethodName}",
                    AutomatedTestClass = parsed.ClassName,
                    AutomatedTestMethod = parsed.MethodName,
                    LastOutcome = parsed.Outcome,
                    LastDurationMs = parsed.DurationMs,
                    LastPipelineRunId = request.PipelineRunId
                });
                newCount++;
            }
        }

        if (isNew)
            await repo.AddSuiteAsync(suite, ct).ConfigureAwait(false);
        else
            await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.LogAsync("IngestedResults", "TestSuite", suite.Id,
            $"{testCases.Count} results ({newCount} new, {updatedCount} updated)", ct).ConfigureAwait(false);

        return new TestIngestionResultDto
        {
            SuiteId = suite.Id,
            TotalCases = testCases.Count,
            NewCases = newCount,
            UpdatedCases = updatedCount
        };
    }

    private static List<ParsedTestCase> ParseTestResults(string xmlContent, string format)
    {
        // F-07/F-04: XXE-safe parsing (DTD/external entities disabled, size-capped) via the shared helper.
        var doc = SafeXml.Load(xmlContent);
        return format.ToLowerInvariant() switch
        {
            "junit" => ParseJUnit(doc),
            "xunit" => ParseXUnit(doc),
            _ => ParseJUnit(doc)
        };
    }

    private static List<ParsedTestCase> ParseJUnit(XDocument doc)
    {
        var results = new List<ParsedTestCase>();
        foreach (var testCase in doc.Descendants("testcase"))
        {
            var className = testCase.Attribute("classname")?.Value ?? "Unknown";
            var methodName = testCase.Attribute("name")?.Value ?? "Unknown";
            var time = double.TryParse(testCase.Attribute("time")?.Value, out var t) ? t * 1000 : (double?)null;

            var outcome = TestOutcome.Passed;
            if (testCase.Element("failure") is not null) outcome = TestOutcome.Failed;
            else if (testCase.Element("error") is not null) outcome = TestOutcome.Error;
            else if (testCase.Element("skipped") is not null) outcome = TestOutcome.Skipped;

            results.Add(new ParsedTestCase(className, methodName, outcome, time));
        }
        return results;
    }

    private static List<ParsedTestCase> ParseXUnit(XDocument doc)
    {
        var results = new List<ParsedTestCase>();
        foreach (var test in doc.Descendants("test"))
        {
            var fullName = test.Attribute("name")?.Value ?? "Unknown.Unknown";
            var lastDot = fullName.LastIndexOf('.');
            var className = lastDot > 0 ? fullName[..lastDot] : "Unknown";
            var methodName = lastDot > 0 ? fullName[(lastDot + 1)..] : fullName;
            var time = double.TryParse(test.Attribute("time")?.Value, out var t) ? t * 1000 : (double?)null;

            var result = test.Attribute("result")?.Value ?? "Pass";
            var outcome = result switch
            {
                "Pass" => TestOutcome.Passed,
                "Fail" => TestOutcome.Failed,
                "Skip" => TestOutcome.Skipped,
                _ => TestOutcome.Passed
            };

            results.Add(new ParsedTestCase(className, methodName, outcome, time));
        }
        return results;
    }

    private static TestSuiteDto MapToDto(TestSuite s) => new()
    {
        Id = s.Id,
        ProjectId = s.ProjectId,
        Name = s.Name,
        Description = s.Description,
        Type = s.Type,
        PipelineId = s.PipelineId,
        PipelineName = s.Pipeline?.Name,
        TestCaseCount = s.TestCases.Count,
        PassedCount = s.TestCases.Count(tc => tc.LastOutcome == TestOutcome.Passed),
        FailedCount = s.TestCases.Count(tc => tc.LastOutcome == TestOutcome.Failed),
        CreatedAt = s.CreatedAt
    };

    private static TestCaseDto MapTestCaseDto(TestCase tc) => new()
    {
        Id = tc.Id,
        TestSuiteId = tc.TestSuiteId,
        Name = tc.Name,
        Description = tc.Description,
        AutomatedTestClass = tc.AutomatedTestClass,
        AutomatedTestMethod = tc.AutomatedTestMethod,
        LastOutcome = tc.LastOutcome,
        LastPipelineRunId = tc.LastPipelineRunId,
        LastDurationMs = tc.LastDurationMs,
        CreatedAt = tc.CreatedAt
    };

    private sealed record ParsedTestCase(string ClassName, string MethodName, TestOutcome Outcome, double? DurationMs);
}
