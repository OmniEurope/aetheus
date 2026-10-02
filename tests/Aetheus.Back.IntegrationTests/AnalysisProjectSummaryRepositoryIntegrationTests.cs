// SPDX-License-Identifier: EUPL-1.2
using System.Data.Common;
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aetheus.Back.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AnalysisProjectSummaryRepositoryIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task GetAsync_UsesAtMostThreePostgresCommands()
    {
        await fixture.ResetAsync();
        var seedOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;
        await using (var seed = new AppDbContext(seedOptions))
        {
            await seed.Database.MigrateAsync(TestContext.Current.CancellationToken);
            var organizationId = await seed.Organizations.OrderBy(item => item.Id)
                .Select(item => item.Id)
                .FirstAsync(TestContext.Current.CancellationToken);
            var project = new Project
            {
                Name = "Summary budget",
                OrganizationId = organizationId,
                DefaultBranch = "main"
            };
            seed.Projects.Add(project);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            var now = DateTime.UtcNow;
            var finding = new AnalysisFinding
            {
                OrganizationId = organizationId,
                ProjectId = project.Id,
                Fingerprint = new string('a', 64),
                RuleId = "security.rule",
                Category = AnalysisCategory.Sast,
                Severity = AnalysisSeverity.Critical,
                Confidence = AnalysisConfidence.High,
                Title = "Finding",
                Message = "Finding",
                Status = AnalysisFindingStatus.Open,
                FirstSeenAt = now.AddMinutes(-2),
                LastSeenAt = now,
                CreatedAt = now.AddMinutes(-2),
                UpdatedAt = now
            };
            var olderReport = new AnalysisReport
            {
                OrganizationId = organizationId,
                ProjectId = project.Id,
                BranchName = "main",
                ScannerKey = "opengrep",
                ScannerName = "OpenGrep",
                ScannerVersion = "1.0",
                Category = AnalysisCategory.Sast,
                Status = AnalysisReportStatus.Passed,
                Format = AnalysisReportFormat.Sarif,
                ContentHash = new string('b', 64),
                StartedAt = now.AddMinutes(-3),
                CompletedAt = now.AddMinutes(-2),
                CreatedAt = now.AddMinutes(-2)
            };
            olderReport.Occurrences.Add(new AnalysisFindingOccurrence
            {
                AnalysisFinding = finding,
                LocationHash = new string('c', 64),
                ToolName = "OpenGrep",
                ScannerKey = "opengrep",
                BranchName = "main",
                RuleId = finding.RuleId,
                Message = finding.Message,
                IsNew = true,
                CreatedAt = now.AddMinutes(-2)
            });
            var latestReport = new AnalysisReport
            {
                OrganizationId = organizationId,
                ProjectId = project.Id,
                ScannerKey = "opengrep",
                ScannerName = "OpenGrep",
                ScannerVersion = "1.0",
                BranchName = "main",
                Category = AnalysisCategory.Sast,
                Status = AnalysisReportStatus.Passed,
                Format = AnalysisReportFormat.Sarif,
                ContentHash = new string('d', 64),
                StartedAt = now.AddMinutes(-1),
                CompletedAt = now,
                CreatedAt = now
            };
            latestReport.Occurrences.Add(new AnalysisFindingOccurrence
            {
                AnalysisFinding = finding,
                LocationHash = new string('c', 64),
                ToolName = "OpenGrep",
                ScannerKey = "opengrep",
                BranchName = "main",
                RuleId = finding.RuleId,
                Message = finding.Message,
                IsNew = false,
                CreatedAt = now
            });
            seed.AnalysisReports.AddRange(olderReport, latestReport);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

            var counter = new CommandCounter();
            var measuredOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .AddInterceptors(counter)
                .Options;
            await using var measured = new AppDbContext(measuredOptions);

            var summary = await new AnalysisProjectSummaryRepository(measured).GetAsync(
                project.Id, TestContext.Current.CancellationToken);

            Assert.Equal(1, summary.OpenCount);
            Assert.Equal(1, summary.CriticalCount);
            Assert.Equal(0, summary.NewCount);
            Assert.NotNull(summary.LastAnalysisAt);
            Assert.InRange(counter.Count, 1, 3);

            var repository = new AnalysisRepository(measured);
            var currentNew = await repository.GetFindingsAsync(
                project.Id,
                new AnalysisFindingPaginationRequest { IsNew = true },
                TestContext.Current.CancellationToken);
            var currentExisting = await repository.GetFindingsAsync(
                project.Id,
                new AnalysisFindingPaginationRequest { IsNew = false },
                TestContext.Current.CancellationToken);

            Assert.Empty(currentNew.Items);
            Assert.Single(currentExisting.Items);
        }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
