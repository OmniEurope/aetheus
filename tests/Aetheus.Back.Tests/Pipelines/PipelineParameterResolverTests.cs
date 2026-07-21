// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Tests;

public class PipelineParameterResolverTests
{
    private static PipelineTemplateParameterDefinition Param(
        string name, string type = "string", string? def = null, bool required = false, params string[] allowed)
        => new() { Name = name, Type = type, Default = def, Required = required, AllowedValues = [.. allowed] };

    [Fact]
    public void TryResolve_SuppliedValue_WinsOverDefault()
    {
        var ok = PipelineParameterResolver.TryResolve(
            [Param("ENV", def: "dev")],
            new Dictionary<string, string> { ["ENV"] = "prod" },
            out var effective, out var errors);

        Assert.True(ok);
        Assert.Empty(errors);
        Assert.Equal("prod", effective["ENV"]);
    }

    [Fact]
    public void TryResolve_NoSuppliedValue_FallsBackToDefault()
    {
        var ok = PipelineParameterResolver.TryResolve([Param("ENV", def: "dev")], null, out var effective, out _);

        Assert.True(ok);
        Assert.Equal("dev", effective["ENV"]);
    }

    [Fact]
    public void TryResolve_RequiredMissingNoDefault_Errors()
    {
        var ok = PipelineParameterResolver.TryResolve([Param("ENV", required: true)], null, out var effective, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("ENV") && e.Contains("required"));
        Assert.False(effective.ContainsKey("ENV"));
    }

    [Fact]
    public void TryResolve_RequiredWithDefault_UsesDefault()
    {
        var ok = PipelineParameterResolver.TryResolve([Param("ENV", def: "dev", required: true)], null, out var effective, out _);

        Assert.True(ok);
        Assert.Equal("dev", effective["ENV"]);
    }

    [Fact]
    public void TryResolve_UnknownParameter_Errors()
    {
        var ok = PipelineParameterResolver.TryResolve(
            [Param("ENV")],
            new Dictionary<string, string> { ["NOPE"] = "x" },
            out _, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("Unknown parameter 'NOPE'"));
    }

    [Fact]
    public void TryResolve_ChoiceOutOfRange_Errors()
    {
        var ok = PipelineParameterResolver.TryResolve(
            [Param("ENV", type: "choice", allowed: ["dev", "prod"])],
            new Dictionary<string, string> { ["ENV"] = "staging" },
            out _, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("ENV") && e.Contains("one of"));
    }

    [Fact]
    public void TryResolve_ChoiceInRange_Ok()
    {
        var ok = PipelineParameterResolver.TryResolve(
            [Param("ENV", type: "choice", allowed: ["dev", "prod"])],
            new Dictionary<string, string> { ["ENV"] = "prod" },
            out var effective, out _);

        Assert.True(ok);
        Assert.Equal("prod", effective["ENV"]);
    }

    [Theory]
    [InlineData("number", "12.5", true)]
    [InlineData("number", "abc", false)]
    [InlineData("boolean", "true", true)]
    [InlineData("boolean", "maybe", false)]
    public void TryResolve_TypedValue_Validates(string type, string value, bool expectedOk)
    {
        var ok = PipelineParameterResolver.TryResolve(
            [Param("P", type: type)],
            new Dictionary<string, string> { ["P"] = value },
            out _, out _);

        Assert.Equal(expectedOk, ok);
    }

    [Fact]
    public void TryResolve_OptionalNoValue_NotInjected()
    {
        var ok = PipelineParameterResolver.TryResolve([Param("OPT")], null, out var effective, out _);

        Assert.True(ok);
        Assert.Empty(effective);
    }

    [Theory]
    [InlineData("AETHEUS_RUN_BRANCH")]
    [InlineData("BUILD_SOURCEVERSION")]
    [InlineData("SYSTEM_TOKEN")]
    public void TryResolve_ReservedSystemName_IsRejected(string name)
    {
        var ok = PipelineParameterResolver.TryResolve(
            [Param(name, def: "attacker")], null, out var effective, out var errors);

        Assert.False(ok);
        Assert.Empty(effective);
        Assert.Contains(errors, error => error.Contains("reserved system variable", StringComparison.Ordinal));
    }

    [Fact]
    public void Defaults_ReturnsOnlyParamsWithDefault()
    {
        var defaults = PipelineParameterResolver.Defaults([Param("A", def: "1"), Param("B"), Param("C", def: "3")]);

        Assert.Equal(2, defaults.Count);
        Assert.Equal("1", defaults["A"]);
        Assert.Equal("3", defaults["C"]);
        Assert.False(defaults.ContainsKey("B"));
    }
}
