// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Pipelines;

namespace Aetheus.Back.Tests.Pipelines;

/// <summary>
/// Lot 11.5: a pipeline definition that carries a program instead of a call. The warning is shown in
/// the launch dialog of every project, so it must fire on what is genuinely unmaintainable (a long
/// block, an embedded heredoc) and stay quiet on an ordinary step, or it will be ignored.
/// </summary>
public class PipelineInlineLogicWarningTests
{
    private static List<string> WarningsFor(string shell)
    {
        var definition = new PipelineYamlDefinition
        {
            Name = "candidate",
            Stages =
            [
                new PipelineStageDefinition
                {
                    Name = "Build",
                    Agent = "linux-01",
                    Steps = [new PipelineStepDefinition { Name = "build", Shell = shell }]
                }
            ]
        };
        var errors = new List<string>();
        var warnings = new List<string>();
        PipelineDefinitionValidator.ValidateStages(definition, errors, warnings);
        Assert.Empty(errors);
        return warnings;
    }

    [Fact]
    public void AShortCallIsNotWarnedAbout()
    {
        var warnings = WarningsFor("""
            set -eu
            export APP_VERSION="1.2.3"
            sh deploy/scripts/publish-application.sh .pipeline-publish
            """);

        Assert.DoesNotContain(warnings, warning => warning.Contains("inlines", StringComparison.Ordinal));
    }

    [Fact]
    public void ALongInlineBlockIsWarnedAboutWithItsLineCount()
    {
        var warnings = WarningsFor(string.Join('\n', Enumerable.Repeat("echo step", 31)));

        var warning = Assert.Single(warnings, item => item.Contains("inlines", StringComparison.Ordinal));
        Assert.Contains("31 lines", warning, StringComparison.Ordinal);
        Assert.Contains("deploy/scripts/", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactlyTheBudgetIsNotWarnedAbout()
    {
        // The boundary is where a warning is most likely to be wrong, so it is pinned.
        var warnings = WarningsFor(string.Join('\n', Enumerable.Repeat("echo step", 30)));

        Assert.DoesNotContain(warnings, warning => warning.Contains("inlines", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("node --input-type=module <<'JS'\nconsole.log(1);\nJS")]
    [InlineData("cat <<EOF > file\nvalue\nEOF")]
    [InlineData("cat <<-\"END\"\nvalue\nEND")]
    public void AHeredocIsWarnedAbout(string shell)
    {
        var warnings = WarningsFor(shell);

        Assert.Single(warnings, warning => warning.Contains("heredoc", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sort file > out")]
    [InlineData("if [ \"$a\" -lt \"$b\" ]; then echo less; fi")]
    [InlineData("echo 'a << b'")]
    public void AnOrdinaryRedirectionOrComparisonIsNotAHeredoc(string shell)
    {
        var warnings = WarningsFor(shell);

        Assert.DoesNotContain(warnings, warning => warning.Contains("heredoc", StringComparison.Ordinal));
    }
}
