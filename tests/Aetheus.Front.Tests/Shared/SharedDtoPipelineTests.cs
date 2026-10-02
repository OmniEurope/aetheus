// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Aetheus.Front.Tests;

public class SharedDtoPipelineTests
{
    [Fact]
    public void PipelineDto_DefaultValues()
    {
        var dto = new PipelineDto();
        Assert.Equal(string.Empty, dto.Name);
        Assert.Equal(string.Empty, dto.Description);
        Assert.Equal(string.Empty, dto.YamlDefinition);
        Assert.Null(dto.ProjectId);
        Assert.Null(dto.LastRunStatus);
        Assert.Null(dto.LastRunAt);
        Assert.Empty(dto.RecentRuns);
    }

    [Fact]
    public void PipelineRunSummaryDto_DefaultValues()
    {
        var dto = new PipelineRunSummaryDto();
        Assert.Equal(0, dto.Id);
        Assert.Null(dto.CompletedAt);
    }

    [Fact]
    public void CreatePipelineRequest_Validation_Valid()
    {
        var req = new CreatePipelineRequest { Name = "Build", YamlDefinition = "stages: []" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    [Fact]
    public void CreatePipelineRequest_Validation_EmptyName_Fails()
    {
        var req = new CreatePipelineRequest { Name = "", YamlDefinition = "yaml" };
        var results = ValidateModel(req);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void UpdatePipelineRequest_Validation_Valid()
    {
        var req = new UpdatePipelineRequest { Name = "Updated", YamlDefinition = "stages: []" };
        var results = ValidateModel(req);
        Assert.Empty(results);
    }

    [Fact]
    public void PipelineRunDto_DefaultValues()
    {
        var dto = new PipelineRunDto();
        Assert.Equal(string.Empty, dto.PipelineName);
        Assert.Null(dto.CompletedAt);
        Assert.Empty(dto.ResolvedVariables);
        Assert.Empty(dto.Warnings);
        Assert.Empty(dto.Steps);
        Assert.Empty(dto.Approvals);
        Assert.Empty(dto.Artifacts);
        Assert.Null(dto.TestResultSummary);
    }

    [Fact]
    public void PipelineStepRunDto_DefaultValues()
    {
        var dto = new PipelineStepRunDto();
        Assert.Equal(string.Empty, dto.StepName);
        Assert.Equal(string.Empty, dto.StageName);
        Assert.Null(dto.CompletedAt);
        Assert.Empty(dto.OutputVariables);
    }

    [Fact]
    public void PipelineApprovalDto_DefaultValues()
    {
        var dto = new PipelineApprovalDto();
        Assert.Equal(0, dto.Id);
        Assert.Equal(string.Empty, dto.StageName);
        Assert.Null(dto.ResolvedAt);
    }

    [Fact]
    public void PipelineArtifactDto_DefaultValues()
    {
        var dto = new PipelineArtifactDto();
        Assert.Equal(string.Empty, dto.Name);
    }

    [Fact]
    public void PipelineTestResultSummaryDto_DefaultValues()
    {
        var dto = new PipelineTestResultSummaryDto();
        Assert.Equal(0, dto.TotalTests);
        Assert.Equal(0, dto.Passed);
        Assert.Equal(0, dto.Failed);
        Assert.Equal(0, dto.Skipped);
    }

    [Fact]
    public void PipelineRunRequest_CurrentShape_Deserializes()
    {
        var request = JsonSerializer.Deserialize<PipelineRunRequest>(
            """{"parameters":{"ENV":"qa"},"sourceBranch":"release/1","idempotencyKey":"request-42"}""");

        Assert.NotNull(request);
        Assert.Equal("qa", request.Parameters!["ENV"]);
        Assert.Equal("release/1", request.SourceBranch);
        Assert.Equal("request-42", request.IdempotencyKey);
    }

    [Fact]
    public void PipelineRunRequest_LegacyDictionary_DeserializesDuringTransition()
    {
        var request = JsonSerializer.Deserialize<PipelineRunRequest>(
            """{"ENV":"qa","AETHEUS_RUN_BRANCH":"release/1","AETHEUS_RUN_IDEMPOTENCY_KEY":"request-42"}""");

        Assert.NotNull(request);
        Assert.Equal("qa", request.Parameters!["ENV"]);
        Assert.False(request.Parameters.ContainsKey("AETHEUS_RUN_BRANCH"));
        Assert.Equal("release/1", request.SourceBranch);
        Assert.Equal("request-42", request.IdempotencyKey);
        Assert.False(request.Parameters.ContainsKey("AETHEUS_RUN_IDEMPOTENCY_KEY"));
    }

    [Fact]
    public void PipelineRunRequest_CurrentShape_RejectsMoreThanSixtyFourParameters()
    {
        var parameters = Enumerable.Range(1, PipelineRunRequest.MaxParameterCount + 1)
            .ToDictionary(index => $"KEY_{index}", _ => "value");
        var json = JsonSerializer.Serialize(new { parameters });

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PipelineRunRequest>(json));
    }

    [Fact]
    public void PipelineRunRequest_LegacyShape_AllowsBranchPlusSixtyFourParameters()
    {
        var values = Enumerable.Range(1, PipelineRunRequest.MaxParameterCount)
            .ToDictionary(index => $"KEY_{index}", _ => "value");
        values["AETHEUS_RUN_BRANCH"] = "release/1";

        var request = JsonSerializer.Deserialize<PipelineRunRequest>(JsonSerializer.Serialize(values));

        Assert.NotNull(request);
        Assert.Equal(PipelineRunRequest.MaxParameterCount, request.Parameters!.Count);
        Assert.Equal("release/1", request.SourceBranch);
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var ctx = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, ctx, results, true);
        return results;
    }
}
