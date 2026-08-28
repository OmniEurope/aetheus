// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// The validation boundary in front of the blue-green operations. These steps mutate a live
/// environment, so a definition that is merely plausible is not enough: the checks here are what
/// stop a bad definition from ever reaching the agent, and they had no test of their own.
/// </summary>
public sealed class BlueGreenStepBindingTests
{
    private static string Identity(string? raw) => (raw ?? string.Empty).Trim();

    private static PipelineStepDefinition ValidStep() => new()
    {
        Name = "Switch",
        Project = "aetheus-demo",
        StateDir = "/var/lib/aetheus-demo",
        EnvFile = "/var/lib/aetheus-demo/.env-demo",
        ComposeFiles = "deploy/compose/base.yml",
        Ports = "10029,10030,10031,10032",
        Revision = "deadbeef",
        ReloadHelper = "/usr/local/lib/aetheus/apache-reload",
        UpstreamTemplate = "/tmp/upstream.template",
        UpstreamConf = "/etc/apache2/sites-available/demo.conf",
        MigrationsDir = "/src/Migrations"
    };

    private static bool Bind(
        OperationKind operation, PipelineStepDefinition step, out BlueGreenStepBinding? binding, out string error) =>
        Bind(operation, step, new Dictionary<string, string>(), out binding, out error);

    private static bool Bind(
        OperationKind operation, PipelineStepDefinition step, Dictionary<string, string> runVariables,
        out BlueGreenStepBinding? binding, out string error) =>
        BlueGreenStepBinding.TryBind(operation, step, Identity, runVariables, out binding, out error);

    /// <summary>
    /// A catch-all default would turn a typo such as <c>bluegreen-swtich</c> into a commit against a
    /// live environment, so an unrecognised type must resolve to nothing rather than be guessed.
    /// </summary>
    [Fact]
    public void OperationFor_RefusesAnUnrecognisedType()
    {
        Assert.Equal(OperationKind.BlueGreenMigrate, BlueGreenStepBinding.OperationFor("bluegreen-migrate"));
        Assert.Equal(OperationKind.BlueGreenUp, BlueGreenStepBinding.OperationFor("bluegreen-up"));
        Assert.Equal(OperationKind.BlueGreenSwitch, BlueGreenStepBinding.OperationFor("bluegreen-switch"));
        Assert.Equal(OperationKind.BlueGreenCommit, BlueGreenStepBinding.OperationFor("bluegreen-commit"));
        Assert.Equal(OperationKind.BlueGreenRollback, BlueGreenStepBinding.OperationFor("bluegreen-rollback"));

        Assert.Equal(OperationKind.None, BlueGreenStepBinding.OperationFor("bluegreen-swtich"));
        Assert.Equal(OperationKind.None, BlueGreenStepBinding.OperationFor("bluegreen-"));
        Assert.Equal(OperationKind.None, BlueGreenStepBinding.OperationFor("smoke"));
    }

    [Fact]
    public void ValidSwitchStep_Binds()
    {
        Assert.True(Bind(OperationKind.BlueGreenSwitch, ValidStep(), out var binding, out var error), error);
        Assert.Equal("aetheus-demo", binding!.Project);
        Assert.Equal("10029", binding.Variables["AETHEUS_BG_PORT_FRONT_BLUE"]);
        Assert.Equal("10032", binding.Variables["AETHEUS_BG_PORT_BACK_GREEN"]);
    }

    // The state directory is where the journal and the deployed revision live; traversal there would
    // let a definition write outside the environment it claims to act on.
    [Theory]
    [InlineData("relative/path")]
    [InlineData("/var/lib/../etc")]
    [InlineData("")]
    public void StateDirectory_MustBeAbsoluteAndFree0fTraversal(string stateDir)
    {
        var step = ValidStep() with { StateDir = stateDir };

        Assert.False(Bind(OperationKind.BlueGreenSwitch, step, out _, out var error));
        Assert.Contains("state_dir", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10029,10030,10031")]
    [InlineData("10029,10030,10031,10029")]
    [InlineData("10029,10030,10031,99999")]
    [InlineData("")]
    public void Ports_MustBeFourDistinctValidValues(string ports)
    {
        var step = ValidStep() with { Ports = ports };

        Assert.False(Bind(OperationKind.BlueGreenSwitch, step, out _, out var error));
        Assert.Contains("ports", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The helper is invoked argv-exact through sudo, so anything that could turn the grant into a
    /// different privileged command has to be refused before a task is created.
    /// </summary>
    [Theory]
    [InlineData("relative/helper")]
    [InlineData("/usr/local/lib/../bin/sh")]
    [InlineData("/usr/local/lib/aetheus/reload extra-arg")]
    [InlineData("")]
    public void ReloadHelper_MustBeAnAbsolutePathWithoutWhitespaceOrTraversal(string helper)
    {
        var step = ValidStep() with { ReloadHelper = helper };

        Assert.False(Bind(OperationKind.BlueGreenSwitch, step, out _, out var error));
        Assert.Contains("reload_helper", error, StringComparison.Ordinal);
    }

    // Rendering the configuration IS the switch; without it the reload re-applies what is already
    // live and the step would report a cutover that never happened.
    [Fact]
    public void Switch_RequiresTheUpstreamTemplateAndDestination()
    {
        Assert.False(Bind(
            OperationKind.BlueGreenSwitch, ValidStep() with { UpstreamTemplate = "" }, out _, out var error));
        Assert.Contains("upstream_template", error, StringComparison.Ordinal);

        Assert.False(Bind(
            OperationKind.BlueGreenSwitch, ValidStep() with { UpstreamConf = "" }, out _, out error));
        Assert.Contains("upstream_template", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Switch_RequiresARevisionForTheJournal()
    {
        Assert.False(Bind(OperationKind.BlueGreenSwitch, ValidStep() with { Revision = "" }, out _, out var error));
        Assert.Contains("revision", error, StringComparison.Ordinal);
    }

    // Without the migration sources the expand/contract gate has nothing to inspect, which is how an
    // earlier revision of this feature lost the check while still claiming to enforce it.
    [Fact]
    public void Migrate_RequiresTheMigrationSources()
    {
        Assert.False(Bind(
            OperationKind.BlueGreenMigrate, ValidStep() with { MigrationsDir = "" }, out _, out var error));
        Assert.Contains("migrations_dir", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Rollback_RequiresWhatItNeedsToRestoreTraffic()
    {
        Assert.False(Bind(
            OperationKind.BlueGreenRollback, ValidStep() with { UpstreamConf = "" }, out _, out var error));
        Assert.Contains("upstream_conf", error, StringComparison.Ordinal);

        Assert.False(Bind(
            OperationKind.BlueGreenRollback, ValidStep() with { ReloadHelper = "" }, out _, out error));
        Assert.Contains("reload_helper", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A shell cutover exported the image tags into the process it then ran Compose from. A typed
    /// step has no such process and the agent service environment does not carry them, so the names
    /// the step lists are the only ones that reach Compose.
    /// </summary>
    [Fact]
    public void ComposeEnv_ForwardsOnlyTheNamedRunVariables()
    {
        var runVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AETHEUS_BACK_IMAGE"] = "aetheus-back:deadbeef",
            ["APP_VERSION"] = "1.1.7",
            ["DEMO_VAULT_SECRET"] = "not-for-compose"
        };
        var step = ValidStep() with { ComposeEnv = "AETHEUS_BACK_IMAGE, APP_VERSION" };

        Assert.True(Bind(OperationKind.BlueGreenUp, step, runVariables, out var binding, out var error), error);
        Assert.Equal(
            "aetheus-back:deadbeef",
            binding!.Variables[BlueGreenStepBinding.ComposeEnvPrefix + "AETHEUS_BACK_IMAGE"]);
        Assert.Equal("1.1.7", binding.Variables[BlueGreenStepBinding.ComposeEnvPrefix + "APP_VERSION"]);
        Assert.DoesNotContain(
            BlueGreenStepBinding.ComposeEnvPrefix + "DEMO_VAULT_SECRET", binding.Variables.Keys);
    }

    /// <summary>
    /// Compose substitutes its own default for a variable it cannot see, so a name the run does not
    /// define would start the placeholder image and still report a successful cutover.
    /// </summary>
    [Fact]
    public void ComposeEnv_RefusesANameTheRunDoesNotDefine()
    {
        var step = ValidStep() with { ComposeEnv = "AETHEUS_BACK_IMAGE" };

        Assert.False(Bind(
            OperationKind.BlueGreenUp, step, new Dictionary<string, string>(), out _, out var error));
        Assert.Contains("AETHEUS_BACK_IMAGE", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PATH=/evil")]
    [InlineData("2INVALID")]
    [InlineData("has space")]
    public void ComposeEnv_RefusesAnEntryThatIsNotAnEnvironmentName(string entry)
    {
        var runVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [entry] = "value" };
        var step = ValidStep() with { ComposeEnv = entry };

        Assert.False(Bind(OperationKind.BlueGreenUp, step, runVariables, out _, out var error));
        Assert.Contains("compose_env", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeEnv_IsOptional()
    {
        Assert.True(Bind(OperationKind.BlueGreenUp, ValidStep(), out var binding, out var error), error);
        Assert.DoesNotContain(
            binding!.Variables.Keys,
            key => key.StartsWith(BlueGreenStepBinding.ComposeEnvPrefix, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Aetheus-Prod")]
    [InlineData("has space")]
    [InlineData("")]
    public void Project_MustBeAValidComposeProject(string project)
    {
        var step = ValidStep() with { Project = project };

        Assert.False(Bind(OperationKind.BlueGreenUp, step, out _, out var error));
        Assert.Contains("project", error, StringComparison.Ordinal);
    }
}
