// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using Aetheus.Back.Components.Analysis;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests.Analysis;

/// <summary>
/// Recette R2-024: the project grade is the grade of its latest candidate run (the root run and the
/// runs its trigger steps started, the worst of them, as on the run page), no longer the latest grade
/// of each domain across every recent run.
/// </summary>
public sealed class AnalysisProjectLatestCandidateGradeTests : IDisposable
{
    private const int ProjectId = 5;
    private readonly AppDbContext _db;
    private readonly DateTime _now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private int _nextId = 1;

    public AnalysisProjectLatestCandidateGradeTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _db.Projects.Add(new Project { Id = ProjectId, Name = "Aetheus", Description = "d", DefaultBranch = "develop" });
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheLatestCandidate_GivesTheGrade_TheWorstOfItsTree_EvenWhenANewerNightlyGradedBetter()
    {
        // Candidate 2478 (sealed) and its CI child 2479, then a newer nightly 2490 graded A on a domain
        // the candidate also measured: the cross-run mix used to take the nightly's A for that domain.
        Run(2478, _now.AddHours(-3), sealedCandidate: true);
        Run(2479, _now.AddHours(-3).AddMinutes(1), parentRunId: 2478);
        Run(2490, _now.AddHours(-1));
        Evaluate(2478, AnalysisGrade.B, AnalysisGradeDomain.CodeQuality, _now.AddHours(-2));
        Evaluate(2479, AnalysisGrade.D, AnalysisGradeDomain.Security, _now.AddHours(-2).AddMinutes(5));
        Evaluate(2490, AnalysisGrade.A, AnalysisGradeDomain.Security, _now.AddMinutes(-30));
        await _db.SaveChangesAsync(Ct);

        var grade = (await new AnalysisProjectSummaryRepository(_db).GetGradesAsync([ProjectId], Ct))[ProjectId];

        Assert.Equal(AnalysisGrade.D, grade.OverallGrade);
        // The run to open is the candidate's root, which shows its children merged.
        Assert.Equal(2478, grade.PipelineRunId);
    }

    [Fact]
    public async Task WithoutAnyCandidate_TheNewestGradedRunTree_GivesTheGrade()
    {
        Run(300, _now.AddHours(-3));
        Run(301, _now.AddHours(-1));
        Run(302, _now.AddHours(-1).AddMinutes(1), parentRunId: 301);
        Evaluate(300, AnalysisGrade.A, AnalysisGradeDomain.Security, _now.AddHours(-2));
        Evaluate(301, AnalysisGrade.A, AnalysisGradeDomain.CodeQuality, _now.AddMinutes(-50));
        Evaluate(302, AnalysisGrade.C, AnalysisGradeDomain.Security, _now.AddMinutes(-40));
        await _db.SaveChangesAsync(Ct);

        var grade = (await new AnalysisProjectSummaryRepository(_db).GetGradesAsync([ProjectId], Ct))[ProjectId];

        Assert.Equal(AnalysisGrade.C, grade.OverallGrade);
        Assert.Equal(301, grade.PipelineRunId);
    }

    [Fact]
    public async Task AProjectNeverGraded_HasNoGrade()
    {
        Run(400, _now);
        await _db.SaveChangesAsync(Ct);

        Assert.Empty(await new AnalysisProjectSummaryRepository(_db).GetGradesAsync([ProjectId], Ct));
    }

    private void Run(int runId, DateTime startedAt, bool sealedCandidate = false, int? parentRunId = null)
    {
        _db.PipelineRuns.Add(new PipelineRun { Id = runId, PipelineId = 1, Status = PipelineStatus.Success, StartedAt = startedAt });
        if (sealedCandidate)
            _db.PipelineStepRuns.Add(new PipelineStepRun
            {
                Id = _nextId++,
                PipelineRunId = runId,
                StageName = "Seal",
                StepName = "assurance",
                Status = TaskExecutionStatus.Success,
                OutputVariablesJson = "{\"CANDIDATE_DEPLOYABLE\":\"false\",\"CANDIDATE_ASSURANCE_GRADE\":\"D\"}"
            });
        if (parentRunId is { } parent)
            _db.PipelineStepRuns.Add(new PipelineStepRun
            {
                Id = _nextId++,
                PipelineRunId = parent,
                StageName = "CI",
                StepName = "trigger",
                Status = TaskExecutionStatus.Success,
                TriggeredRunId = runId
            });
    }

    private void Evaluate(int runId, AnalysisGrade grade, AnalysisGradeDomain domain, DateTime evaluatedAt)
    {
        var observed = grade switch
        {
            AnalysisGrade.A => 100,
            AnalysisGrade.B => 85,
            AnalysisGrade.C => 75,
            _ => 65
        };
        var snapshot = new AnalysisGradeSummaryDto
        {
            EvaluatedAt = evaluatedAt,
            OverallGrade = grade,
            Completeness = AnalysisGradeCompleteness.Complete,
            Domains =
            [
                new AnalysisGradeDomainDto
                {
                    Domain = domain,
                    Grade = grade,
                    Required = true,
                    Completeness = AnalysisGradeCompleteness.Complete,
                    EvaluatedAt = evaluatedAt,
                    Measures =
                    [
                        new AnalysisGradeMeasureDto
                        {
                            Key = domain.ToString(),
                            Domain = domain,
                            Grade = grade,
                            Required = true,
                            Observed = true,
                            ObservedValue = observed,
                            Direction = AnalysisMetricDirection.HigherIsBetter,
                            AThreshold = 90,
                            BThreshold = 80,
                            CThreshold = 70,
                            DThreshold = 60,
                            EThreshold = 50
                        }
                    ]
                }
            ]
        };
        var id = _nextId++;
        _db.AnalysisEvaluations.Add(new AnalysisEvaluation
        {
            Id = id,
            ProjectId = ProjectId,
            AnalysisReportId = id,
            PipelineRunId = runId,
            Status = AnalysisGateStatus.Passed,
            Grade = grade,
            GradeCompleteness = AnalysisGradeCompleteness.Complete,
            GradeSnapshotJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            EvaluatedAt = evaluatedAt,
            CreatedAt = evaluatedAt
        });
    }
}
