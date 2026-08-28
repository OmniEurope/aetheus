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
        var installer = ReadInstaller();

        Assert.Contains(
            "User=$AGENT_USER\nGroup=$AGENT_GROUP\nUMask=0077\nWorkingDirectory=$INSTALL_DIR",
            installer.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
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

        Assert.Contains(
            "APACHE_RELOAD_HELPER_PATH=\"/usr/local/lib/aetheus/aetheus-apache-reload\"",
            installer, StringComparison.Ordinal);
        Assert.Contains(
            "[ \"$#\" -eq 0 ] || { echo \"aetheus-apache-reload accepts no arguments\" >&2; exit 2; }",
            installer, StringComparison.Ordinal);
        Assert.Contains(
            "/usr/sbin/apache2ctl configtest || { echo \"aetheus-apache-reload: configuration rejected; nothing applied\" >&2; exit 1; }",
            installer, StringComparison.Ordinal);
        Assert.Contains("exec /bin/systemctl reload apache2.service", installer, StringComparison.Ordinal);
        Assert.Contains("$APACHE_RELOAD_HELPER_PATH", installer, StringComparison.Ordinal);
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
    public void MailManage_UsesLiteralKeyFilteringAndOwnsBothDkimOutputs()
    {
        var installer = ReadInstaller();

        Assert.Contains("without_space_key() { awk -v key=\"$1\" '$1 != key'", installer, StringComparison.Ordinal);
        Assert.Contains("without_colon_key() { awk -F: -v key=\"$1\" '$1 != key'", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("grep -v \"^$email ", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("grep -v \"^$email:", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("grep -v \"^\\*@$domain ", installer, StringComparison.Ordinal);
        Assert.Contains("\"/etc/opendkim/keys/$selector.private\" \\", installer, StringComparison.Ordinal);
        Assert.Contains("\"/etc/opendkim/keys/$selector.txt\"", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 644 \"/etc/opendkim/keys/$selector.txt\"", installer, StringComparison.Ordinal);
    }

    private static string ReadInstaller() =>
        File.ReadAllText(Path.Combine(RepoRoot, "deploy", "scripts", "install-agent-linux.sh"));

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
