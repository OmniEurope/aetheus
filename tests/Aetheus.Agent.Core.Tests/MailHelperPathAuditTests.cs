// SPDX-License-Identifier: EUPL-1.2
using System.Text.RegularExpressions;
using Aetheus.Agent.Core.Operations;

namespace Aetheus.Agent.Core.Tests;

/// <summary>
/// Drift guard for the S-FEAT-W8KN mail helpers. <see cref="MailOperationExecutor.SetupHelperPath"/>
/// and <see cref="MailOperationExecutor.ManageHelperPath"/> are hard-coded in the agent AND must be
/// (a) written as root-owned helpers by <c>install-agent-linux.sh</c> and (b) granted in the
/// <c>/etc/sudoers.d/aetheus-mail</c> drop-in. If the agent shells out to a binary the installer
/// never deposits (the exact <c>mail-manage</c> gap this guard was added for), every incremental mail
/// op fails at runtime with no compile-time signal. This test resolves the script's shell variables
/// and asserts the path contract, the heredoc that writes each helper, and the sudoers grant.
/// </summary>
public sealed class MailHelperPathAuditTests
{
    [Fact]
    public void SetupHelperPath_matches_install_script()
        => Assert.Equal(MailOperationExecutor.SetupHelperPath, ResolveHelperPath("MAIL_SETUP_HELPER_PATH"));

    [Fact]
    public void ManageHelperPath_matches_install_script()
        => Assert.Equal(MailOperationExecutor.ManageHelperPath, ResolveHelperPath("MAIL_MANAGE_HELPER_PATH"));

    [Theory]
    [InlineData("MAIL_SETUP_HELPER_PATH")]
    [InlineData("MAIL_MANAGE_HELPER_PATH")]
    public void Each_helper_is_written_by_a_heredoc(string varName)
    {
        var script = ReadInstallScript();
        Assert.Matches(new Regex($@"cat > ""\${Regex.Escape(varName)}""", RegexOptions.None), script);
    }

    [Fact]
    public void Sudoers_grants_both_mail_helpers()
    {
        var script = ReadInstallScript();
        Assert.Contains("Cmnd_Alias AETHEUS_MAIL = $MAIL_SETUP_HELPER_PATH", script, StringComparison.Ordinal);
        Assert.Contains("Cmnd_Alias AETHEUS_MAIL_MANAGE = $MAIL_MANAGE_HELPER_PATH", script, StringComparison.Ordinal);
        // The NOPASSWD grant must reference both aliases, else mail-manage is written but unusable.
        Assert.Matches(new Regex(@"NOPASSWD:\s*AETHEUS_MAIL\b.*\bAETHEUS_MAIL_MANAGE\b"), script);
    }

    // Resolve a `$AETHEUS_HELPER_DIR/...`-shaped helper path var against the script's AETHEUS_HELPER_DIR.
    private static string ResolveHelperPath(string varName)
    {
        var script = ReadInstallScript();
        var helperDir = ResolveVar(script, "AETHEUS_HELPER_DIR");
        return ResolveVar(script, varName).Replace("$AETHEUS_HELPER_DIR", helperDir, StringComparison.Ordinal);
    }

    private static string ResolveVar(string script, string name)
    {
        var m = Regex.Match(script, $@"^{Regex.Escape(name)}=""([^""]*)""", RegexOptions.Multiline);
        Assert.True(m.Success, $"{name}=\"…\" not found in install-agent-linux.sh");
        return m.Groups[1].Value;
    }

    private static string ReadInstallScript()
        => File.ReadAllText(Path.Combine(FindRepoRoot(), "deploy", "scripts", "install-agent-linux.sh"));

    private static string FindRepoRoot() => Aetheus.Agent.Core.Tests.RepositoryScan.Root;
}
