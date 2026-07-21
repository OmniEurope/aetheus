// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests;

public class SharedDtoInstantiationTests
{
    [Fact]
    public void PullRequestDiffDto_DefaultValues()
    {
        var dto = new PullRequestDiffDto();
        Assert.Empty(dto.FileDiffs);
        Assert.NotNull(dto.Stats);
    }

    [Fact]
    public void PullRequestDiffDto_WithValues()
    {
        var dto = new PullRequestDiffDto
        {
            FileDiffs = [new FileDiffDto { Path = "file.cs", Status = "modified", Additions = 5, Deletions = 2, Patch = "diff" }],
            Stats = new DiffStatsDto { Additions = 5, Deletions = 2, FilesChanged = 1 }
        };
        Assert.Single(dto.FileDiffs);
        Assert.Equal(5, dto.Stats.Additions);
    }

    [Fact]
    public void ProjectActivityDto_Properties()
    {
        var dto = new ProjectActivityDto
        {
            Type = "pipeline",
            Title = "Build #42",
            Status = "success",
            Timestamp = DateTime.UtcNow,
            Icon = "check"
        };
        Assert.Equal("pipeline", dto.Type);
        Assert.Equal("Build #42", dto.Title);
        Assert.Equal("success", dto.Status);
        Assert.Equal("check", dto.Icon);
    }

    [Fact]
    public void PipelineTemplateSummaryDto_Properties()
    {
        var dto = new PipelineTemplateSummaryDto
        {
            Id = 1,
            Name = "CI",
            Description = "desc",
            Category = "Build",
            Version = 2
        };
        Assert.Equal(1, dto.Id);
        Assert.Equal("CI", dto.Name);
        Assert.Equal(2, dto.Version);
    }

    [Fact]
    public void PipelineTemplateParameterDefinition_DefaultsAndValues()
    {
        var dto = new PipelineTemplateParameterDefinition();
        Assert.Equal("string", dto.Type);
        Assert.Empty(dto.AllowedValues);

        var dto2 = new PipelineTemplateParameterDefinition
        {
            Name = "env",
            Type = "choice",
            Default = "prod",
            AllowedValues = ["dev", "staging", "prod"]
        };
        Assert.Equal("env", dto2.Name);
        Assert.Equal(3, dto2.AllowedValues.Count);
    }

    [Fact]
    public void PipelineTemplateDto_Properties()
    {
        var dto = new PipelineTemplateDto
        {
            Id = 1,
            Name = "T",
            Description = "d",
            Category = "C",
            YamlContent = "yaml:",
            Version = 3,
            Changelog = "fix"
        };
        Assert.Equal("yaml:", dto.YamlContent);
        Assert.Equal(3, dto.Version);
    }

    [Fact]
    public void PipelineDeploymentStrategy_Defaults()
    {
        var dto = new PipelineDeploymentStrategy();
        Assert.Equal("runOnce", dto.Type);
        Assert.Equal(1, dto.MaxParallel);
    }

    [Fact]
    public void PipelineDeploymentStrategy_CustomValues()
    {
        var dto = new PipelineDeploymentStrategy { Type = "rolling", MaxParallel = 3 };
        Assert.Equal("rolling", dto.Type);
        Assert.Equal(3, dto.MaxParallel);
    }

    [Fact]
    public void GitLightTagDto_Properties()
    {
        var dto = new GitLightTagDto
        {
            Name = "v1.0",
            Sha = "abc123",
            Message = "Release",
            TaggerName = "user",
            TaggerDate = DateTime.UtcNow
        };
        Assert.Equal("v1.0", dto.Name);
        Assert.Equal("abc123", dto.Sha);
    }

    [Fact]
    public void GitLightBlameLine_Properties()
    {
        var dto = new GitLightBlameLine
        {
            LineNumber = 1,
            Sha = "abc",
            ShortSha = "ab",
            AuthorName = "dev",
            AuthorDate = DateTime.UtcNow,
            Line = "code"
        };
        Assert.Equal(1, dto.LineNumber);
        Assert.Equal("code", dto.Line);
    }

    [Fact]
    public void FileDiffDto_Properties()
    {
        var dto = new FileDiffDto { Path = "src/a.cs", Status = "added", Additions = 10, Deletions = 0, Patch = "+new" };
        Assert.Equal("src/a.cs", dto.Path);
        Assert.Equal(10, dto.Additions);
    }

    [Fact]
    public void ExternalLoginRequest_Properties()
    {
        var dto = new ExternalLoginRequest
        {
            Provider = "GitHub",
            SubjectId = "12345",
            DisplayName = "User",
            Email = "user@example.com"
        };
        Assert.Equal("GitHub", dto.Provider);
        Assert.Equal("12345", dto.SubjectId);
    }

    [Fact]
    public void EnvironmentServerDto_Properties()
    {
        var dto = new EnvironmentServerDto { ServerId = 1, ServerName = "web1", ServerStatus = ServerStatus.Online };
        Assert.Equal(ServerStatus.Online, dto.ServerStatus);
    }

    [Fact]
    public void EnvironmentCheckDto_Properties()
    {
        var dto = new EnvironmentCheckDto
        {
            Id = 1,
            EnvironmentId = 2,
            Name = "Health",
            Type = EnvironmentCheckType.RestCallback,
            Configuration = "{}",
            IsRequired = true,
            TimeoutSeconds = 60,
            CreatedAt = DateTime.UtcNow
        };
        Assert.Equal(EnvironmentCheckType.RestCallback, dto.Type);
        Assert.True(dto.IsRequired);
    }

    [Fact]
    public void EffectivePermissionDto_Properties()
    {
        var dto = new EffectivePermissionDto
        {
            ResourceType = ResourceType.Server,
            ResourceId = 1,
            ResourceName = "web1",
            Permission = Permission.Read,
            GrantedByRole = "Admin"
        };
        Assert.Equal(ResourceType.Server, dto.ResourceType);
        Assert.Equal("Admin", dto.GrantedByRole);
    }

    [Fact]
    public void DockerShellRequest_Defaults()
    {
        var dto = new DockerShellRequest();
        Assert.Equal("/bin/sh", dto.Shell);
    }

    [Fact]
    public void DockerShellRequest_CustomValues()
    {
        var dto = new DockerShellRequest { ContainerId = "abc123", Shell = "/bin/bash" };
        Assert.Equal("abc123", dto.ContainerId);
        Assert.Equal("/bin/bash", dto.Shell);
    }

    [Fact]
    public void DockerFileEntryDto_Properties()
    {
        var dto = new DockerFileEntryDto { Name = "app.log", Type = "file", Size = "1.2MB", Permissions = "rwxr-xr-x" };
        Assert.Equal("app.log", dto.Name);
        Assert.Equal("file", dto.Type);
    }

    [Fact]
    public void DockerComposeFileDto_Properties()
    {
        var dto = new DockerComposeFileDto { StackName = "myapp", Content = "version: '3'" };
        Assert.Equal("myapp", dto.StackName);
    }

    [Fact]
    public void DiffStatsDto_Properties()
    {
        var dto = new DiffStatsDto { Additions = 10, Deletions = 5, FilesChanged = 3 };
        Assert.Equal(10, dto.Additions);
        Assert.Equal(3, dto.FilesChanged);
    }

    [Fact]
    public void CreatePipelineTemplateRequest_Properties()
    {
        var dto = new CreatePipelineTemplateRequest
        {
            Name = "CI Template",
            Description = "desc",
            Category = "Build",
            YamlContent = "stages:"
        };
        Assert.Equal("CI Template", dto.Name);
    }

    [Fact]
    public void UpdatePipelineTemplateRequest_Properties()
    {
        var dto = new UpdatePipelineTemplateRequest
        {
            Name = "Updated",
            Description = "new desc",
            Category = "Deploy",
            YamlContent = "stages:",
            ChangelogEntry = "fix"
        };
        Assert.Equal("Updated", dto.Name);
        Assert.Equal("fix", dto.ChangelogEntry);
    }

    [Fact]
    public void CreateGitLightTagRequest_Properties()
    {
        var dto = new CreateGitLightTagRequest { Name = "v1.0", Ref = "main", Message = "Release 1.0" };
        Assert.Equal("v1.0", dto.Name);
        Assert.Equal("main", dto.Ref);
    }

    [Fact]
    public void CreateGitLightBranchRequest_Properties()
    {
        var dto = new CreateGitLightBranchRequest { Name = "feature/new", StartRef = "main" };
        Assert.Equal("feature/new", dto.Name);
    }

    [Fact]
    public void ApacheVHostConfigDto_Properties()
    {
        var dto = new ApacheVHostConfigDto { SiteName = "example.com", Content = "<VirtualHost>" };
        Assert.Equal("example.com", dto.SiteName);
    }

    [Fact]
    public void WebhookPayload_Properties()
    {
        var dto = new WebhookPayload { Ref = "refs/heads/main", RepositoryUrl = "https://github.com/org/repo" };
        Assert.Equal("refs/heads/main", dto.Ref);
    }

    [Fact]
    public void GitLightBranchDto_Properties()
    {
        var dto = new GitLightBranchDto
        {
            Name = "main",
            IsDefault = true,
            LastCommitSha = "abc",
            LastCommitDate = DateTime.UtcNow,
            LastCommitMessage = "init"
        };
        Assert.True(dto.IsDefault);
        Assert.Equal("main", dto.Name);
    }

    [Fact]
    public void GitLightBranchDto_Defaults()
    {
        var dto = new GitLightBranchDto();
        Assert.False(dto.IsDefault);
        Assert.Null(dto.LastCommitSha);
        Assert.Null(dto.LastCommitDate);
    }
}
