// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// The launch check that refuses a reference nothing in the run can ever provide. It had no test of
/// its own. What matters is both directions: a missing library entry must still be refused, and a
/// name the run does provide later - including one a script publishes, which the step text cannot
/// show - must not be.
/// </summary>
public class PipelineUnresolvedVariableGuardTests
{
    private static readonly IReadOnlySet<string> NoSecrets = new HashSet<string>();

    private static PipelineYamlDefinition Definition(params PipelineStepDefinition[] steps) => new()
    {
        Name = "deploy",
        Stages = [new PipelineStageDefinition { Name = "Prepare", Steps = [.. steps] }]
    };

    private static Dictionary<string, string> Resolved(params (string Key, string Value)[] entries)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries) resolved[key] = value;
        return resolved;
    }

    [Fact]
    public void AReferenceNothingProvides_IsReportedWithWhatReferencesIt()
    {
        var failures = PipelineUnresolvedVariableGuard.FindUnprovidableNames(
            Definition(new PipelineStepDefinition { Name = "render", Shell = "echo $(DOCS_DOMAIN)" }),
            Resolved(("VHOST", "$(API_DOMAIN).conf")),
            NoSecrets);

        Assert.Equal(["API_DOMAIN", "DOCS_DOMAIN"], failures.Keys);
        Assert.Equal(["variable VHOST"], failures["API_DOMAIN"]);
        Assert.Equal(["step 'render'"], failures["DOCS_DOMAIN"]);
    }

    /// <summary>PLAN-004 R-02: the launch preflight leaves a pipeline name still carrying a reference
    /// to the step, so a reference nothing provides has to be refused here or it reaches dispatch.</summary>
    [Fact]
    public void ATriggeredOrArtifactSourcePipelineNameNothingProvides_IsReported()
    {
        var failures = PipelineUnresolvedVariableGuard.FindUnprovidableNames(
            Definition(
                new PipelineStepDefinition { Name = "run ci", Type = "trigger", Pipeline = "$(APPLICATON_CI_PIPELINE)" },
                new PipelineStepDefinition
                {
                    Name = "release",
                    Type = "release",
                    ArtifactSourcePipeline = "$(APPLICATION_CI_PIPELINE)"
                },
                new PipelineStepDefinition
                {
                    Name = "restore",
                    Type = "restore-artifacts",
                    ArtifactSourcePipeline = "$(PICKED_PIPELINE)",
                    Outputs = ["PICKED_PIPELINE"]
                }),
            Resolved(("APPLICATION_CI_PIPELINE", "aetheus-ci")),
            NoSecrets);

        Assert.Equal(["APPLICATON_CI_PIPELINE"], failures.Keys);
        Assert.Equal(["step 'run ci'"], failures["APPLICATON_CI_PIPELINE"]);
    }

    [Fact]
    public void Validate_RefusesTheLaunch()
    {
        var error = Assert.Throws<BadRequestException>(() => PipelineUnresolvedVariableGuard.Validate(
            Definition(), Resolved(("VHOST", "$(API_DOMAIN).conf")), NoSecrets));

        Assert.Contains("API_DOMAIN", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamePublishedByAnInlineDirective_IsAccepted()
    {
        var failures = PipelineUnresolvedVariableGuard.FindUnprovidableNames(
            Definition(new PipelineStepDefinition
            {
                Name = "prepare",
                Shell = "echo \"##aetheus[setvariable name=SOURCE_COMMIT]$(git rev-parse HEAD)\""
            }),
            Resolved(("IMAGE", "smoke:$(SOURCE_COMMIT)")),
            NoSecrets);

        Assert.Empty(failures);
    }

    /// <summary>
    /// D-02: prod-deploy-prepare.sh publishes SOURCE_COMMIT from inside the script, so the step text
    /// shows no directive and the name used to be refused. The step now says what it publishes.
    /// </summary>
    [Fact]
    public void ANameAStepDeclaresUnderOutputs_IsAccepted()
    {
        var withoutDeclaration = PipelineUnresolvedVariableGuard.FindUnprovidableNames(
            Definition(new PipelineStepDefinition { Name = "prepare", Shell = "sh deploy/scripts/prod-deploy-prepare.sh" }),
            Resolved(("IMAGE", "smoke:$(SOURCE_COMMIT)")),
            NoSecrets);
        var withDeclaration = PipelineUnresolvedVariableGuard.FindUnprovidableNames(
            Definition(new PipelineStepDefinition
            {
                Name = "prepare",
                Shell = "sh deploy/scripts/prod-deploy-prepare.sh",
                Outputs = ["SOURCE_COMMIT", "AETHEUS_BACK_IMAGE"]
            }),
            Resolved(("IMAGE", "smoke:$(SOURCE_COMMIT)")),
            NoSecrets);

        Assert.Equal(["SOURCE_COMMIT"], withoutDeclaration.Keys);
        Assert.Empty(withDeclaration);
    }

    /// <summary>D-03: a reference carrying a default always resolves to something.</summary>
    [Fact]
    public void AReferenceWithADefault_IsNeverReported()
    {
        var failures = PipelineUnresolvedVariableGuard.FindUnprovidableNames(
            Definition(new PipelineStepDefinition { Name = "tag", Shell = "echo $(RELEASE_CHANNEL:-stable) $(MISSING)" }),
            Resolved(("VERSION", "1.1.$(RELEASE_COUNTER:-0)")),
            NoSecrets);

        Assert.Equal(["MISSING"], failures.Keys);
    }

    [Fact]
    public void ASecretValue_IsNotReadAsATemplate()
    {
        var failures = PipelineUnresolvedVariableGuard.FindUnprovidableNames(
            Definition(),
            Resolved(("DB_PASSWORD", "p$(NOT_A_REFERENCE)")),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DB_PASSWORD" });

        Assert.Empty(failures);
    }

    [Fact]
    public void BuildDeferredNames_CoversParametersControlPlaneAndBothPublicationForms()
    {
        var definition = Definition(
            new PipelineStepDefinition { Name = "a", Shell = "echo '##aetheus[setvariable name=INLINE_NAME]x'" },
            new PipelineStepDefinition { Name = "b", Shell = "sh publish.sh", Outputs = ["SCRIPT_NAME"] }) with
        {
            Parameters = [new PipelineTemplateParameterDefinition { Name = "revision", Type = "string" }]
        };

        var deferred = PipelineUnresolvedVariableGuard.BuildDeferredNames(definition);

        Assert.Contains("INLINE_NAME", deferred);
        Assert.Contains("SCRIPT_NAME", deferred);
        Assert.Contains("revision", deferred);
        Assert.Contains("AGENT_NAME", deferred);
        Assert.DoesNotContain("API_DOMAIN", deferred);
    }
}
