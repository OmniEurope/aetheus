// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.VariableLibraries;
using Aetheus.Back.Components.Vaults;
using Aetheus.Back.Data.Entities;
using Aetheus.Shared.DTOs;
using NSubstitute;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// Covers <see cref="PipelineVariableResolver.ResolveFullVariablesForRunAsync"/> - the per-run
/// variable set a pipeline run executes against. Libraries/vaults are empty here so the test stays
/// focused on the system + project + additional variable plumbing (and the $(VAR) substitution pass).
/// </summary>
public class PipelineVariableResolverTests
{
    private readonly IVariableLibraryService _varLib = Substitute.For<IVariableLibraryService>();
    private readonly IVaultService _vault = Substitute.For<IVaultService>();
    private readonly IPipelineRepository _repo = Substitute.For<IPipelineRepository>();
    private readonly PipelineVariableResolver _sut;

    public PipelineVariableResolverTests()
        => _sut = new PipelineVariableResolver(
            _varLib, _vault, _repo, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), TimeProvider.System);

    private static PipelineRun NewRun(Project? project, string additionalVarsJson = "{}", string? commitHash = null) => new()
    {
        Id = 42,
        PipelineId = 7,
        CommitHash = commitHash,
        AdditionalVariablesJson = additionalVarsJson,
        Pipeline = new Pipeline { Id = 7, Name = "build", ProjectId = project is null ? null : 1, Project = project }
    };

    [Fact]
    public async Task ResolveFullVariablesForRunAsync_InjectsSystemAndProjectVariables()
    {
        var run = NewRun(new Project { Name = "App", RepositoryUrl = "https://repo", DefaultBranch = "develop", OrganizationId = 1 });
        var def = new PipelineYamlDefinition { Name = "build", Trigger = "manual" };

        var vars = await _sut.ResolveFullVariablesForRunAsync(run, def, TestContext.Current.CancellationToken);

        Assert.Equal("42", vars["BUILD_BUILDID"]);
        Assert.Equal("7", vars["BUILD_PIPELINEID"]);
        Assert.Equal("1", vars["BUILD_PROJECTID"]);
        Assert.Equal("App", vars["BUILD_PROJECTNAME"]);
        Assert.Equal("https://repo", vars["REPOSITORY_URL"]);
        Assert.Equal("develop", vars["DEFAULT_BRANCH"]);
        Assert.Equal("true", vars["CI"]);
    }

    [Fact]
    public async Task ResolveFullVariablesForRunAsync_RunBranchOverridesProjectBranchEverywhere()
    {
        var run = NewRun(new Project
        {
            Name = "App",
            RepositoryUrl = "https://repo",
            DefaultBranch = "main",
            OrganizationId = 1
        });
        run.BranchName = "develop";

        var vars = await _sut.ResolveFullVariablesForRunAsync(
            run, new PipelineYamlDefinition { Name = "nightly" }, TestContext.Current.CancellationToken);

        Assert.Equal("develop", vars["DEFAULT_BRANCH"]);
        Assert.Equal("develop", vars["BUILD_SOURCEBRANCH"]);
    }

    [Fact]
    public async Task ResolveFullVariablesForRunAsync_ExposesProjectType_WhenDeclared()
    {
        // S-TECH-53: a declared project_type surfaces as PROJECT_TYPE; absent when not declared.
        var run = NewRun(new Project { Name = "App", OrganizationId = 1 });
        var withType = await _sut.ResolveFullVariablesForRunAsync(
            run, new PipelineYamlDefinition { Name = "build", ProjectType = "dotnet" }, TestContext.Current.CancellationToken);
        Assert.Equal("dotnet", withType["PROJECT_TYPE"]);

        var withoutType = await _sut.ResolveFullVariablesForRunAsync(
            run, new PipelineYamlDefinition { Name = "build" }, TestContext.Current.CancellationToken);
        Assert.False(withoutType.ContainsKey("PROJECT_TYPE"));
    }

    [Fact]
    public async Task ResolveFullVariablesForRunAsync_MergesAdditionalVariables()
    {
        var run = NewRun(project: null, additionalVarsJson: "{\"MY_VAR\":\"hello\"}");
        var def = new PipelineYamlDefinition { Name = "build", Trigger = "manual" };

        var vars = await _sut.ResolveFullVariablesForRunAsync(run, def, TestContext.Current.CancellationToken);

        Assert.Equal("hello", vars["MY_VAR"]);
    }

    [Fact]
    public async Task ResolveFullVariablesForRunAsync_ReinjectsPinnedSourceRevisionAfterPrepare()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        var run = NewRun(project: null, additionalVarsJson: "{\"BUILD_SOURCEVERSION\":\"untrusted\"}", commitHash: commit);
        var def = new PipelineYamlDefinition { Name = "build", Trigger = "manual" };

        var vars = await _sut.ResolveFullVariablesForRunAsync(run, def, TestContext.Current.CancellationToken);

        Assert.Equal(commit, vars["BUILD_SOURCEVERSION"]);
    }

    [Fact]
    public async Task ResolveFullVariablesForRunAsync_SubstitutesNestedTokens()
    {
        var run = NewRun(new Project { Name = "App", DefaultBranch = "main", OrganizationId = 1 });
        var def = new PipelineYamlDefinition
        {
            Name = "build",
            Trigger = "manual",
            Variables = new Dictionary<string, string> { ["TAG"] = "v-$(BUILD_BUILDID)" }
        };

        var vars = await _sut.ResolveFullVariablesForRunAsync(run, def, TestContext.Current.CancellationToken);

        Assert.Equal("v-42", vars["TAG"]);
    }

    [Fact]
    public async Task ResolveFullVariablesForRunAsync_MapsDeclaredDiagnosticParameterIntoProtectedVariable()
    {
        var run = NewRun(project: null, additionalVarsJson: "{\"releaseLabFailQa\":\"true\",\"parameters.releaseLabFailQa\":\"true\"}");
        var def = new PipelineYamlDefinition
        {
            Name = "release",
            Trigger = "manual",
            Parameters =
            [
                new PipelineTemplateParameterDefinition
                {
                    Name = "releaseLabFailQa",
                    Type = "boolean",
                    Default = "false"
                }
            ],
            Variables = new Dictionary<string, string>
            {
                ["AETHEUS_RELEASE_LAB_FAIL_QA"] = "$(releaseLabFailQa)"
            }
        };

        var vars = await _sut.ResolveFullVariablesForRunAsync(run, def, TestContext.Current.CancellationToken);

        Assert.Equal("true", vars["releaseLabFailQa"]);
        Assert.Equal("true", vars["AETHEUS_RELEASE_LAB_FAIL_QA"]);
    }
}
