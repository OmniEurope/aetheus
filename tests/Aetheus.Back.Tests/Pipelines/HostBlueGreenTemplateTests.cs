// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Data;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// The reusable host cutover template. It is what the deployment pipelines will extend instead of
/// each carrying their own shell copy of the same sequence, so a mistake here would propagate to
/// every project that adopts it rather than to one.
///
/// These tests exist because the template is inert until something extends it: nothing else would
/// catch a misspelled field or an unknown step type before the day it is first used in anger.
/// </summary>
public sealed class HostBlueGreenTemplateTests
{
    /// <summary>
    /// The version a consuming pipeline should extend. v1 stays published and immutable; every
    /// contract below is asserted against the current one, so a new version cannot quietly drop one.
    /// </summary>
    private const string Resource = "host-bluegreen-deploy-v2.yaml";

    private static PipelineYamlDefinition Parse() =>
        YamlParsingHelper.Deserializer.Deserialize<PipelineYamlDefinition>(
            DeliveryPipelineTemplateSeeder.ReadResource(Resource));

    [Fact]
    public void Template_ParsesAndValidatesCleanly()
    {
        var definition = Parse();
        var errors = new List<string>();
        var warnings = new List<string>();

        PipelineDefinitionValidator.ValidateDefinitionBasics(definition, errors, warnings);
        PipelineDefinitionValidator.ValidateStages(definition, errors, warnings);

        Assert.Empty(errors);
    }

    // A typed step carries no shell, so an unknown or misspelled type would be rejected by the
    // validator as a step with nothing to run. Asserting the exact set also pins that the template
    // stayed free of shell.
    [Fact]
    public void EveryStep_UsesANativeTypeAndNoShell()
    {
        var steps = Parse().Stages.SelectMany(stage => stage.Steps).ToList();

        Assert.All(steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Type), $"Step '{step.Name}' has no type.");
            Assert.True(string.IsNullOrWhiteSpace(step.Shell), $"Step '{step.Name}' still carries shell.");
        });
        Assert.Equal(
            ["bluegreen-commit", "bluegreen-migrate", "bluegreen-rollback", "bluegreen-switch", "bluegreen-up", "smoke"],
            steps.Select(step => step.Type!).Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The commit must come after the evidence stage. That ordering is the whole point of splitting
    /// the cutover: it is what stops a probe finding from undoing a deployment that is already
    /// serving correctly, which is exactly what the shell implementation used to do.
    /// </summary>
    [Fact]
    public void Commit_DependsOnTheEvidenceStage()
    {
        var stages = Parse().Stages;
        var commit = Assert.Single(stages, stage => stage.Name == "Commit");
        var evidence = Assert.Single(stages, stage => stage.Name == "Evidence");

        Assert.Contains("Evidence", commit.DependsOn);
        Assert.Contains("Switch", evidence.DependsOn);
    }

    /// <summary>
    /// The rollback must name the LAST stage it guards, not the first. A failed() stage resolves its
    /// dependencies against terminal stages, so its condition is answered as soon as the stage it
    /// names settles: naming `Switch` answered it the moment the cutover succeeded and retired the
    /// rollback while the run was still green, leaving the evidence stage after the cutover with no
    /// compensation at all. Naming `Commit` keeps it pending through Evidence and Commit and fires it
    /// on a failure of either, while still never reaching it on a healthy run.
    /// </summary>
    [Fact]
    public void Rollback_IsAFailureHandlerOnTheLastStageItGuards()
    {
        var stages = Parse().Stages;
        var rollback = Assert.Single(stages, stage => stage.Name == "Rollback");

        Assert.Equal("failed()", rollback.Condition);
        Assert.Equal(["Commit"], rollback.DependsOn);
        // Nothing may run after the stage the rollback guards, or that stage would fail outside its
        // window and the rollback would be answered before it.
        Assert.DoesNotContain(
            stages.Where(stage => stage.Name != "Rollback"),
            stage => stage.DependsOn.Contains("Commit"));
    }

    // Rendering the upstream configuration IS the switch; without it the reload would re-apply the
    // configuration already live and report a successful cutover that never happened.
    [Fact]
    public void Switch_RendersTheUpstreamConfiguration()
    {
        var step = Assert.Single(
            Parse().Stages.SelectMany(stage => stage.Steps),
            candidate => candidate.Type == "bluegreen-switch");

        Assert.False(string.IsNullOrWhiteSpace(step.UpstreamTemplate));
        Assert.False(string.IsNullOrWhiteSpace(step.UpstreamConf));
        Assert.False(string.IsNullOrWhiteSpace(step.ReloadHelper));
        Assert.False(string.IsNullOrWhiteSpace(step.Revision));
    }

    /// <summary>
    /// The evidence stage has to be able to fail, or the rollback below it is decorative: a failed()
    /// stage only fires on a failed stage, and the smoke executor exits 0 in advisory mode however bad
    /// the findings are.
    /// </summary>
    [Fact]
    public void Evidence_IsGradedAsBlockingSoTheRollbackHasATrigger()
    {
        var definition = Parse();
        var smoke = Assert.Single(
            definition.Stages.SelectMany(stage => stage.Steps),
            candidate => candidate.Type == "smoke");

        Assert.Equal("$(BG_SMOKE_GATE)", smoke.EvidenceGate);
        Assert.Equal("blocking", definition.Variables["BG_SMOKE_GATE"]);
    }

    // Without the migration sources the expand/contract gate silently has nothing to inspect, which
    // is how the previous rewrite lost the check while still claiming to enforce it.
    [Fact]
    public void Migrate_PointsAtTheMigrationSources()
    {
        var step = Assert.Single(
            Parse().Stages.SelectMany(stage => stage.Steps),
            candidate => candidate.Type == "bluegreen-migrate");

        Assert.False(string.IsNullOrWhiteSpace(step.MigrationsDir));
    }

    /// <summary>
    /// Nothing in the template may name a single application: it is meant to be extended by any
    /// project, and a hard-coded host or project would silently bind it to one.
    /// </summary>
    [Fact]
    public void Template_CarriesNoApplicationSpecificIdentity()
    {
        var yaml = DeliveryPipelineTemplateSeeder.ReadResource(Resource);

        foreach (var token in new[] { "aetheus-prod", "aetheus-demo", "sonytumen", "10025", "10029" })
            Assert.DoesNotContain(token, yaml, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every Compose invocation in the cutover must receive the same per-run values. A step left
    /// without them would run the same project against the Compose file's defaults - a placeholder
    /// image started under the banner of a successful cutover, which is exactly the drift the typed
    /// steps replaced.
    /// </summary>
    [Fact]
    public void EveryBlueGreenStep_CarriesTheRunsComposeInputs()
    {
        var definition = Parse();
        var blueGreenSteps = definition.Stages
            .SelectMany(stage => stage.Steps)
            .Where(step => step.Type?.StartsWith("bluegreen-", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(5, blueGreenSteps.Length);
        Assert.All(blueGreenSteps, step => Assert.Equal("$(BG_COMPOSE_ENV)", step.ComposeEnv));
        Assert.Equal(string.Empty, definition.Variables["BG_COMPOSE_ENV"]);
    }

    /// <summary>
    /// v1 is published and immutable - the seeder refuses a version whose content changed - so it is
    /// pinned here rather than merely left alone. It also documents what v2 adds.
    /// </summary>
    [Fact]
    public void PublishedV1_StaysWithoutTheComposeInputsThatV2Adds()
    {
        var v1 = DeliveryPipelineTemplateSeeder.ReadResource("host-bluegreen-deploy-v1.yaml");

        Assert.DoesNotContain("BG_COMPOSE_ENV", v1, StringComparison.Ordinal);
        Assert.Contains("type: bluegreen-switch", v1, StringComparison.Ordinal);
    }
}
