// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

public sealed class LinuxAgentInstallerHardeningAuditTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void HealthCheck_RequiresInitialCompatibilityHeartbeatBeforeSuccess()
    {
        var installer = ReadInstaller();

        Assert.Contains("Initial heartbeat sent; agent contract is ready", installer, StringComparison.Ordinal);
        Assert.Contains("_hc_enrolled", installer, StringComparison.Ordinal);
        Assert.Contains("_hc_contract_ready", installer, StringComparison.Ordinal);
        Assert.Matches(
            @"# Positive signals\.\s+if \[ \""\$_hc_enrolled\"" -eq 1 \] && \[ \""\$_hc_contract_ready\"" -eq 1 \]; then[\s\S]*?return 0\s+fi",
            installer);
    }

    [Fact]
    public void ServiceUnit_CreatesAgentFilesWithPrivateUmask()
    {
        // R-249: the unit is the agent/aetheus-agent.service template the installer renders.
        Assert.Contains(
            "render_host_config agent/aetheus-agent.service \"$SYSTEMD_UNIT_PATH\"",
            ReadInstaller(),
            StringComparison.Ordinal);
        Assert.Contains(
            "User=#{AGENT_USER}#\nGroup=#{AGENT_GROUP}#\nUMask=0077\nWorkingDirectory=#{INSTALL_DIR}#",
            LinuxHostConfigTemplates.Read("agent/aetheus-agent.service"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// R-249: an agent without any sudo grant runs with NoNewPrivileges=true. Commit df6895eb9 had
    /// dropped the sandboxed branch's NNP_BLOCK, so that unit carried an empty line instead. Renders
    /// the unit template's hardening section with the installer's own sandboxed-branch value and
    /// requires the restored line right after the header, as before df6895eb9.
    /// </summary>
    [Fact]
    public void ServiceUnit_WithoutElevation_RendersNoNewPrivilegesTrue()
    {
        var installer = ReadInstaller().Replace("\r\n", "\n", StringComparison.Ordinal);
        var unitWriter = installer[installer.IndexOf("write_systemd_unit() {", StringComparison.Ordinal)..];
        var sandboxed = unitWriter[unitWriter.IndexOf("    else\n", StringComparison.Ordinal)..];
        sandboxed = sandboxed[..sandboxed.IndexOf("\n    fi\n", StringComparison.Ordinal)];
        var assignment = System.Text.RegularExpressions.Regex.Match(sandboxed, "NNP_BLOCK=\"([^\"]*)\"");
        Assert.True(assignment.Success, "the sandboxed branch of write_systemd_unit must set NNP_BLOCK");

        var rendered = LinuxHostConfigTemplates.Read("agent/aetheus-agent.service")
            .Replace("#{NNP_BLOCK}#", assignment.Groups[1].Value, StringComparison.Ordinal);

        Assert.Contains(
            "# --- Security hardening ---\n"
            + "# Fully sandboxed: no sudo grant, so privilege escalation is blocked outright.\n"
            + "NoNewPrivileges=true\n"
            + "#{PROTECT_SYSTEM_BLOCK}#\n",
            rendered,
            StringComparison.Ordinal);
        // The elevated branch is unchanged: it still relaxes the flag for the sudo grant.
        Assert.Contains("NoNewPrivileges=false\"", unitWriter, StringComparison.Ordinal);
    }

    [Fact]
    public void ApacheManage_PreservesSharedHostVhosts()
    {
        var installer = ReadInstaller();

        Assert.Contains("chmod +t \"$d\"", installer, StringComparison.Ordinal);
        Assert.Contains("setfacl -x \"d:u:$AGENT_USER\" \"$d\"", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("setfacl -d -m \"u:$AGENT_USER:rwx\"", installer, StringComparison.Ordinal);
        Assert.Contains("head -n 1 \"$f\" | grep -Fq '# Managed by Aetheus'", installer, StringComparison.Ordinal);
        Assert.Contains("chown -h \"$AGENT_USER:$AGENT_GROUP\" \"$enabled\"", installer, StringComparison.Ordinal);
    }

    /// <summary>
    /// The typed blue-green switch invokes its reload helper as <c>sudo -n &lt;path&gt;</c> with argv
    /// exactly one element, so it cannot reach the three-word <c>systemctl reload</c> grant. This
    /// pins the dedicated helper: no arguments, a configtest that refuses to apply a rejected
    /// configuration (a reload that returns success without applying anything would report a cutover
    /// that never happened), and a sudoers entry naming its exact absolute path.
    /// </summary>
    [Fact]
    public void ApacheReloadHelper_ValidatesBeforeApplyingAndIsGrantedByExactPath()
    {
        var installer = ReadInstaller();
        // R-249: the helper and its sudoers drop-in are templates the installer renders.
        var helper = LinuxHostConfigTemplates.Read("apache/aetheus-apache-reload");
        var sudoers = LinuxHostConfigTemplates.Read("apache/sudoers.d/aetheus-apache");

        Assert.Contains(
            "APACHE_RELOAD_HELPER_PATH=\"/usr/local/lib/aetheus/aetheus-apache-reload\"",
            installer, StringComparison.Ordinal);
        Assert.Contains(
            "render_host_config apache/aetheus-apache-reload \"$APACHE_RELOAD_HELPER_PATH\"",
            installer, StringComparison.Ordinal);
        Assert.Contains(
            "[ \"$#\" -eq 0 ] || { echo \"aetheus-apache-reload accepts no arguments\" >&2; exit 2; }",
            helper, StringComparison.Ordinal);
        Assert.Contains(
            "/usr/sbin/apache2ctl configtest || { echo \"aetheus-apache-reload: configuration rejected; nothing applied\" >&2; exit 1; }",
            helper, StringComparison.Ordinal);
        Assert.Contains("exec /bin/systemctl reload apache2.service", helper, StringComparison.Ordinal);
        Assert.Contains("#{APACHE_RELOAD_HELPER_PATH}#", sudoers, StringComparison.Ordinal);
        Assert.Contains("chown root:root \"$APACHE_RELOAD_HELPER_PATH\"", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 755 \"$APACHE_RELOAD_HELPER_PATH\"", installer, StringComparison.Ordinal);
        // A rejected sudoers file must leave no helper behind that nothing is allowed to invoke.
        Assert.Contains("rm -f \"$APACHE_RELOAD_HELPER_PATH\"", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void ApacheAndCertbot_KeepStrictSystemProtectionWithTargetedWritablePaths()
    {
        var installer = ReadInstaller();

        Assert.Contains("PROTECT_SYSTEM_BLOCK=\"ProtectSystem=strict\"", installer, StringComparison.Ordinal);
        Assert.Contains("READ_WRITE_PATHS_BLOCK=\"$READ_WRITE_PATHS_BLOCK /etc/apache2\"", installer, StringComparison.Ordinal);
        Assert.Contains(
            "mkdir -p /etc/letsencrypt /var/lib/letsencrypt /var/log/letsencrypt",
            installer,
            StringComparison.Ordinal);
        Assert.Contains(
            "/etc/letsencrypt /var/lib/letsencrypt /var/log/letsencrypt",
            installer,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "package/mail/deploy/apache-manage/certbot-manage grants write system paths",
            installer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Homes_StayHidden_SaveTheSnapDataDirectoryCertbotNeeds()
    {
        // Snap certbot cannot start without /root/snap (nightly 2503, 2026-10-02); nothing wider opens.
        var installer = ReadInstaller();
        var unit = LinuxHostConfigTemplates.Read("agent/aetheus-agent.service");

        Assert.Contains("#{PROTECT_HOME_BLOCK}#", unit, StringComparison.Ordinal);
        Assert.Contains("PROTECT_HOME_BLOCK=\"ProtectHome=true\"", installer, StringComparison.Ordinal);
        Assert.Contains("PROTECT_HOME_BLOCK=\"ProtectHome=tmpfs\nBindPaths=-/root/snap\"", installer, StringComparison.Ordinal);
        // The self-update worker hides the homes too: a bare mkdir there aborted the upgrade (2026-10-03).
        Assert.Contains("if ! mkdir -p /root/snap 2>/dev/null; then", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("\n        mkdir -p /root/snap\n", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("ProtectHome=false", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("ProtectHome=read-only", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void MailManage_UsesLiteralKeyFilteringAndOwnsBothDkimOutputs()
    {
        // R-249: the helper is the mail/mail-manage template the installer renders.
        Assert.Contains(
            "render_host_config mail/mail-manage \"$MAIL_MANAGE_HELPER_PATH\"",
            ReadInstaller(),
            StringComparison.Ordinal);
        var installer = LinuxHostConfigTemplates.Read("mail/mail-manage");

        Assert.Contains("without_space_key() { awk -v key=\"$1\" '$1 != key'", installer, StringComparison.Ordinal);
        Assert.Contains("without_colon_key() { awk -F: -v key=\"$1\" '$1 != key'", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("grep -v \"^$email ", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("grep -v \"^$email:", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("grep -v \"^\\*@$domain ", installer, StringComparison.Ordinal);
        // PLAN-005: keys live per domain (two domains may share a selector); both outputs stay owned by
        // opendkim, the private key 600 and the public .txt 644 so the unprivileged agent can inventory it.
        Assert.Contains("chown -R opendkim:opendkim \"/etc/opendkim/keys/$d\"", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 600 \"/etc/opendkim/keys/$d/$s.private\"", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 644 \"/etc/opendkim/keys/$d/$s.txt\"", installer, StringComparison.Ordinal);
    }

    private static string ReadInstaller() =>
        File.ReadAllText(Path.Combine(RepoRoot, "deploy", "scripts", "install-agent-linux.sh"));

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
