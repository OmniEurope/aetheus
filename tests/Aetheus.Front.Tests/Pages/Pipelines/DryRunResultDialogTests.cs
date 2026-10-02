// SPDX-License-Identifier: EUPL-1.2
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class DryRunResultDialogTests : BunitContext
{
    public DryRunResultDialogTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_EmptyResult()
    {
        var result = new DryRunResultDto
        {
            Stages = [],
            ResolvedVariables = [],
            Warnings = []
        };
        var cut = Render<DryRunResultDialog>(p => p.Add(x => x.Result, result));

        Assert.Contains("ResolvedVariables", cut.Markup);
        Assert.Contains("(0)", cut.Markup);
    }

    [Fact]
    public void Renders_NullResult()
    {
        var cut = Render<DryRunResultDialog>(p => p.Add(x => x.Result, null));

        Assert.DoesNotContain("ResolvedVariables", cut.Markup);
    }

    [Fact]
    public void Renders_Warnings()
    {
        var result = new DryRunResultDto
        {
            Warnings = ["Missing variable 'DB_HOST'", "Agent offline"],
            Stages = [],
            ResolvedVariables = []
        };
        var cut = Render<DryRunResultDialog>(p => p.Add(x => x.Result, result));

        Assert.Contains("Missing variable", cut.Markup);
        Assert.Contains("Agent offline", cut.Markup);
    }

    [Fact]
    public void Renders_ResolvedVariables()
    {
        var result = new DryRunResultDto
        {
            ResolvedVariables = new Dictionary<string, string>
            {
                ["DB_HOST"] = "localhost",
                ["DB_PORT"] = "5432"
            },
            Stages = [],
            Warnings = []
        };
        var cut = Render<DryRunResultDialog>(p => p.Add(x => x.Result, result));

        Assert.Contains("(2)", cut.Markup);
    }

    [Fact]
    public void Renders_StagesWithSteps()
    {
        var result = new DryRunResultDto
        {
            Stages =
            [
                new DryRunStageDto
                {
                    StageName = "build",
                    Agent = "server-1",
                    Steps =
                    [
                        new DryRunStepDto
                        {
                            StepName = "compile",
                            OriginalCommand = "dotnet build $(PROJECT)",
                            ResolvedCommand = "dotnet build MyApp.csproj"
                        }
                    ]
                }
            ],
            ResolvedVariables = [],
            Warnings = []
        };
        var cut = Render<DryRunResultDialog>(p => p.Add(x => x.Result, result));

        Assert.Contains("build", cut.Markup);
        Assert.Contains("server-1", cut.Markup);
    }

    [Fact]
    public void Renders_MultipleStages()
    {
        var result = new DryRunResultDto
        {
            Stages =
            [
                new DryRunStageDto { StageName = "build", Agent = "s1", Steps = [] },
                new DryRunStageDto { StageName = "deploy", Agent = "s2", Steps = [] }
            ],
            ResolvedVariables = [],
            Warnings = []
        };
        var cut = Render<DryRunResultDialog>(p => p.Add(x => x.Result, result));

        Assert.Contains("build", cut.Markup);
        Assert.Contains("deploy", cut.Markup);
    }
}
