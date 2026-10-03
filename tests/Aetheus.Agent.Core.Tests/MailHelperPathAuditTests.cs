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
/// and asserts the path contract, the template rendered into each helper (R-249), and the sudoers grant.
/// </summary>
public sealed class MailHelperPathAuditTests
{
    [Fact]
    public void SetupHelperPath_matches_install_script()
        => Assert.Equal(MailOperationExecutor.SetupHelperPath, ResolveHelperPath("MAIL_SETUP_HELPER_PATH"));

    [Fact]
    public void ManageHelperPath_matches_install_script()
        => Assert.Equal(MailOperationExecutor.ManageHelperPath, ResolveHelperPath("MAIL_MANAGE_HELPER_PATH"));

    // R-249: each helper is a template under deploy/agent-host-config the installer renders to its path.
    [Theory]
    [InlineData("MAIL_SETUP_HELPER_PATH", "mail/mail-setup")]
    [InlineData("MAIL_MANAGE_HELPER_PATH", "mail/mail-manage")]
    public void Each_helper_is_rendered_from_its_template(string varName, string template)
    {
        var script = ReadInstallScript();
        Assert.Matches(
            new Regex($@"render_host_config {Regex.Escape(template)} ""\${Regex.Escape(varName)}""", RegexOptions.None),
            script);
        Assert.StartsWith("#!/bin/sh\n", LinuxHostConfigTemplates.Read(template), StringComparison.Ordinal);
    }

    [Fact]
    public void Sudoers_grants_both_mail_helpers()
    {
        Assert.Contains(
            "render_host_config mail/sudoers.d/aetheus-mail \"$MAIL_MANAGE_SUDOERS_FILE\"",
            ReadInstallScript(),
            StringComparison.Ordinal);
        var script = LinuxHostConfigTemplates.Read("mail/sudoers.d/aetheus-mail");
        Assert.Contains("Cmnd_Alias AETHEUS_MAIL = #{MAIL_SETUP_HELPER_PATH}#", script, StringComparison.Ordinal);
        Assert.Contains("Cmnd_Alias AETHEUS_MAIL_MANAGE = #{MAIL_MANAGE_HELPER_PATH}#", script, StringComparison.Ordinal);
        // The NOPASSWD grant must reference both aliases, else mail-manage is written but unusable.
        Assert.Matches(new Regex(@"NOPASSWD:\s*AETHEUS_MAIL\b.*\bAETHEUS_MAIL_MANAGE\b"), script);
    }

    [Fact]
    public void Setup_gives_postfix_a_main_cf_before_the_first_postconf()
    {
        // Task 23162 (2026-10-03): a Postfix installed as "No configuration" has no main.cf, and the
        // first postconf -e died on it. The helper preseeds debconf and falls back to the packaged default.
        var script = LinuxHostConfigTemplates.Read("mail/mail-setup");
        var firstPostconf = script.IndexOf("postconf -e", StringComparison.Ordinal);
        Assert.True(script.IndexOf("postfix postfix/main_mailer_type select Internet Site", StringComparison.Ordinal) is > 0 and var preseed
            && preseed < script.IndexOf("apt-get install", StringComparison.Ordinal));
        Assert.True(script.IndexOf("/usr/share/postfix/main.cf.debian /etc/postfix/main.cf", StringComparison.Ordinal) is > 0 and var seed
            && seed < firstPostconf);
        // IPv4 only (2026-10-03): one reverse DNS record, the IPv4 one, is all the host needs.
        Assert.Contains("postconf -e \"inet_protocols = ipv4\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Setup_unmasks_the_stack_before_enabling_it()
    {
        // Tasks 23257 and 23258 (2026-10-03): a postfix unit masked on the host refused enable.
        var script = LinuxHostConfigTemplates.Read("mail/mail-setup");
        var unmask = script.IndexOf("systemctl unmask postfix dovecot opendkim", StringComparison.Ordinal);
        Assert.True(unmask > 0 && unmask < script.IndexOf("systemctl enable postfix dovecot opendkim", StringComparison.Ordinal));
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
