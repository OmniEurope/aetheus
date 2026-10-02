// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;

namespace Aetheus.Back.Tests.Architecture;

/// <summary>
/// Architectural guard: no delivery script may contain control-plane substitution syntax.
///
/// Two forms of it read as ordinary text but mean entirely different things depending on where they
/// live. In a pipeline's <c>shell:</c> block, <c>$(NAME)</c> is a pipeline variable and
/// <c>${{ parameters.x }}</c> a template expression, both substituted before the agent ever runs the
/// command. Inside a <c>.sh</c> file the control plane never sees them: <c>$(NAME)</c> becomes a
/// POSIX command substitution that tries to execute a command called NAME, and
/// <c>${{ ... }}</c> is a bad substitution that aborts the script on its first line.
///
/// This is not hypothetical. Extracting the inline shell out of the pipelines (PLAN-006 lot 11)
/// carried both forms into scripts by hand: <c>$(QA_PORT_BACK)</c> would have broken the QA rollback
/// health check, and three <c>${{ parameters.* }}</c> reads left in
/// <c>seal-assurance-contract.sh</c> would have aborted the step that seals every candidate's
/// assurance contract, making no candidate deployable at all.
///
/// Neither form is caught by <c>sh -n</c>: both parse cleanly and fail only when expanded. So the
/// guard has to be static, and it has to be here rather than in a shell one-liner nobody reruns.
/// The fix is always the same: pass the value by name from the calling step
/// (<c>NAME="$(NAME)" sh script.sh</c>) and read <c>${NAME}</c> in the script.
/// </summary>
public class ShellScriptControlPlaneSyntaxAuditTests
{
    /// <summary>`$(NAME)` where NAME is a bare uppercase identifier. Deliberately narrow: a real
    /// command substitution such as `$(cat file)` or `$(sh helper.sh)` contains a space or a
    /// lowercase letter, so it never matches. The control-plane default form `$(NAME:-value)` is the
    /// same mistake - in a script it runs a command called `NAME:-value` - so it matches too.</summary>
    private static readonly Regex PipelineVariable = new(
        @"\$\([A-Z][A-Z0-9_]*(?::-[^()]*)?\)", RegexOptions.Compiled);

    [Theory]
    [InlineData("tag=\"$(IMAGE_TAG)\"", true)]
    [InlineData("tag=\"$(IMAGE_TAG:-latest)\"", true)]
    [InlineData("tag=\"$(IMAGE_TAG:-)\"", true)]
    [InlineData("now=\"$(date -u)\"", false)]
    [InlineData("tag=\"${IMAGE_TAG:-latest}\"", false)]
    public void PipelineVariablePattern_CatchesBothControlPlaneForms(string line, bool offending)
        => Assert.Equal(offending, PipelineVariable.IsMatch(line));

    private static readonly Regex TemplateExpression = new(
        @"\$\{\{", RegexOptions.Compiled);

    [Fact]
    public void NoDeliveryScript_ContainsControlPlaneSubstitution()
    {
        var scriptsDir = Path.Combine(RepositoryScan.Root, "deploy", "scripts");
        Assert.True(Directory.Exists(scriptsDir), $"Scripts dir not found: {scriptsDir}");

        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in RepositoryScan.Enumerate(scriptsDir, "*.sh"))
        {
            scanned++;
            var relative = Path.GetRelativePath(RepositoryScan.Root, file);
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                // A header comment documents the trap on purpose and must stay readable.
                if (line.TrimStart().StartsWith('#')) continue;
                if (PipelineVariable.IsMatch(line))
                    offenders.Add($"{relative}:{lineNumber} (pipeline variable) {line.Trim()}");
                if (TemplateExpression.IsMatch(line))
                    offenders.Add($"{relative}:{lineNumber} (template expression) {line.Trim()}");
            }
        }

        Assert.True(scanned >= 50, $"Scanner found only {scanned} shell scripts - likely a path bug.");
        Assert.True(
            offenders.Count == 0,
            "Control-plane substitution syntax only works inside a pipeline's shell: block. In a .sh "
            + "file $(NAME) runs a command called NAME and ${{ ... }} is a bad substitution that "
            + "aborts the script. Pass the value by name from the calling step (NAME=\"$(NAME)\" sh "
            + "script.sh) and read ${NAME} here. Offenders:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}
