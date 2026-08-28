// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Tests.Architecture;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// `compose_env` names the run variables a blue-green step hands to Compose, and the agent fails the
/// step closed when the run does not define one of them, so Compose cannot silently fall back to its
/// own default. That guard collides with how the run-scoped bootstrap identity is produced: the
/// control plane derives it for OperationKind.BlueGreenUp alone, because the credential has to be in
/// the container environment at start-up and every later step acts on a colour that already carries
/// it.
///
/// A pipeline that lists that identity on the other blue-green steps is therefore refused at its
/// first cutover step. aetheus-deploy-prod did exactly that, and nothing noticed because the pipeline
/// had never been run: its Migrate stage failed immediately with "'compose_env' names 'ADMIN_USER',
/// which this run does not define".
///
/// These tests read the shipped definitions, because that is where the mistake lives - the agent and
/// the control plane each behaved as documented.
/// </summary>
public sealed class BlueGreenComposeEnvContractTests
{
    // The names the control plane injects for the starting step and for nothing else. Mirrors
    // PipelineHostOperationTaskFactory.TryAddDeploymentBootstrapIdentity. Deliberately not ADMIN_*:
    // the deployed backend seeds its persisted administrator from Auth:AdminPassword, so the
    // run-scoped secret must never travel under that name.
    private static readonly string[] StartOnlyNames =
    [
        "DEPLOYMENT_BOOTSTRAP_USER", "DEPLOYMENT_BOOTSTRAP_PASSWORD",
        "BOOTSTRAP_STAMP", "DEPLOYMENT_BOOTSTRAP_EXPIRES_AT_UTC"
    ];

    public static TheoryData<string> DeploymentPipelines() =>
    [
        Path.Combine(".pipeline", "aetheus-deploy-prod.yaml"),
        Path.Combine(".pipeline", "aetheus-nightly.yaml")
    ];

    [Fact]
    public void TemplateVersionThree_UsesTheCutoverListOnEveryStepThatDoesNotStartAColour()
    {
        var template = File.ReadAllText(Path.Combine(
            RepositoryScan.Root, "deploy", "pipeline-templates", "host-bluegreen-deploy-v3.yaml"));

        foreach (var (type, expected) in new[]
        {
            ("bluegreen-migrate", "$(BG_COMPOSE_ENV_CUTOVER)"),
            ("bluegreen-switch", "$(BG_COMPOSE_ENV_CUTOVER)"),
            ("bluegreen-commit", "$(BG_COMPOSE_ENV_CUTOVER)"),
            ("bluegreen-rollback", "$(BG_COMPOSE_ENV_CUTOVER)"),
            ("bluegreen-up", "$(BG_COMPOSE_ENV)")
        })
        {
            var actual = ReadComposeEnvFor(template, type);
            Assert.True(
                actual == expected,
                $"{type} should hand Compose {expected} but the template says {actual ?? "nothing"}.");
        }
    }

    [Fact]
    public void CutoverList_DefaultsToTheFullListSoExistingConsumersAreUnaffected()
    {
        var template = File.ReadAllText(Path.Combine(
            RepositoryScan.Root, "deploy", "pipeline-templates", "host-bluegreen-deploy-v3.yaml"));

        Assert.Equal("$(BG_COMPOSE_ENV)", ReadVariable(template, "BG_COMPOSE_ENV_CUTOVER"));
    }

    /// <summary>
    /// v4 gives the rollback its own list. It fires on `failed()`, which includes a run that failed
    /// before the step publishing the image tags ever ran, and the guard then refuses the one
    /// compensation the template has. Nightly run 1202 failed exactly that way: the rollback reported
    /// "'compose_env' names 'AETHEUS_BACK_IMAGE', which this run does not define" on a run whose
    /// preparation step had been skipped.
    /// </summary>
    [Fact]
    public void TemplateVersionFour_GivesTheRollbackItsOwnList()
    {
        var template = File.ReadAllText(Path.Combine(
            RepositoryScan.Root, "deploy", "pipeline-templates", "host-bluegreen-deploy-v4.yaml"));

        foreach (var (type, expected) in new[]
        {
            ("bluegreen-migrate", "$(BG_COMPOSE_ENV_CUTOVER)"),
            ("bluegreen-switch", "$(BG_COMPOSE_ENV_CUTOVER)"),
            ("bluegreen-commit", "$(BG_COMPOSE_ENV_CUTOVER)"),
            ("bluegreen-rollback", "$(BG_COMPOSE_ENV_ROLLBACK)"),
            ("bluegreen-up", "$(BG_COMPOSE_ENV)")
        })
        {
            var actual = ReadComposeEnvFor(template, type);
            Assert.True(
                actual == expected,
                $"{type} should hand Compose {expected} but the template says {actual ?? "nothing"}.");
        }

        // Same reasoning as the cutover list: a consumer whose Compose values are all static must be
        // able to adopt v4 without naming anything new.
        Assert.Equal("$(BG_COMPOSE_ENV_CUTOVER)", ReadVariable(template, "BG_COMPOSE_ENV_ROLLBACK"));
    }

    /// <summary>Reads the compose_env of the step declaring the given type, whichever order the keys sit in.</summary>
    private static string? ReadComposeEnvFor(string yaml, string stepType)
    {
        var lines = yaml.Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim() == $"type: {stepType}");
        if (start < 0) return null;
        for (var i = start + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("- name:", StringComparison.Ordinal) || trimmed.StartsWith("type:", StringComparison.Ordinal))
                break;
            if (trimmed.StartsWith("compose_env:", StringComparison.Ordinal))
                return trimmed["compose_env:".Length..].Trim().Trim('"');
        }
        return null;
    }

    private static string? ReadVariable(string yaml, string name)
        => yaml.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith($"{name}:", StringComparison.Ordinal))
            .Select(line => line[(name.Length + 1)..].Trim().Trim('"'))
            .FirstOrDefault();

    private static string[] Split(string list)
        => list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
