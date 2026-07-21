// SPDX-License-Identifier: EUPL-1.2

using YamlDotNet.Serialization;

namespace Aetheus.Back.Tests.Architecture;

public sealed class DeploymentSafetyAuditTests
{
    private static string Root => FindRepoRoot();
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [Fact]
    public void BlueGreenPortsAreLoopbackOnly_AndApplicationInstancesSkipMigrations()
    {
        var compose = Read("deploy", "compose", "remote-bluegreen.compose.yml");
        foreach (var port in new[] { "PORT_BACK_BLUE", "PORT_BACK_GREEN", "PORT_FRONT_BLUE", "PORT_FRONT_GREEN" })
            Assert.Contains($"127.0.0.1:${{{port}", compose, StringComparison.Ordinal);
        Assert.Contains("Database__SkipMigrations=true", compose, StringComparison.Ordinal);
        Assert.Contains("AETHEUS_RUN_MIGRATIONS=false", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallersFailClosed_AndWindowsDoesNotUseLocalSystem()
    {
        var windows = Read("deploy", "scripts", "install-agent-windows.ps1");
        Assert.Contains("NT SERVICE\\$ServiceName", windows, StringComparison.Ordinal);
        Assert.Contains("Agent enrollment could not be proven", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("registration could not be confirmed from logs", windows, StringComparison.Ordinal);
        Assert.Contains("DockerStorageMaintenance", windows, StringComparison.Ordinal);
        Assert.Contains("PolicyVersion = 2", windows, StringComparison.Ordinal);
        Assert.Contains("DryRun = $false", windows, StringComparison.Ordinal);
        Assert.Contains("DeploymentOnly = $false", windows, StringComparison.Ordinal);
        Assert.Contains("AllowBuildsOnDeploymentTarget = $false", windows, StringComparison.Ordinal);
        Assert.Contains("${serviceIdentity}:(OI)(CI)M", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("${serviceIdentity}:(OI)(CI)RX", windows, StringComparison.Ordinal);

        var linux = Read("deploy", "scripts", "install-agent-linux.sh");
        Assert.Contains("No successful enrollment outcome was observed", linux, StringComparison.Ordinal);
        Assert.Contains("rm -f \"$CERTBOT_MANAGE_SUDOERS_FILE\" \"$TEAMSPEAK_SETUP_SUDOERS_FILE\"", linux, StringComparison.Ordinal);
        Assert.Contains("gpasswd -d \"$AGENT_USER\" docker", linux, StringComparison.Ordinal);
        Assert.Contains("gpasswd -d \"$AGENT_USER\" teamspeak", linux, StringComparison.Ordinal);
        Assert.Contains("\"DockerStorageMaintenance\"", linux, StringComparison.Ordinal);
        Assert.Contains("\"PolicyVersion\": 2", linux, StringComparison.Ordinal);
        Assert.Contains("\"DryRun\": false", linux, StringComparison.Ordinal);
        Assert.Contains("DOCKER_STORAGE_DEPLOYMENT_ONLY=true", linux, StringComparison.Ordinal);
        Assert.Contains("\"AllowBuildsOnDeploymentTarget\": false", linux, StringComparison.Ordinal);
    }

    [Fact]
    public void CertbotIssueHelper_UsesWebrootInProduction_AndSelfSignsOnlyInLocalMode()
    {
        var script = Read("deploy", "scripts", "install-agent-linux.sh");
        var start = script.IndexOf("cat > \"$CERTBOT_ISSUE_HELPER_PATH\"", StringComparison.Ordinal);
        var end = script.IndexOf("\nHELPER_EOF", start + 10, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var helper = script[start..end];
        var productionStart = helper.IndexOf("if [ \"$mode\" = production ]; then", StringComparison.Ordinal);
        var localStart = helper.IndexOf("# Local-only certificate", productionStart + 1, StringComparison.Ordinal);
        Assert.True(productionStart >= 0 && localStart > productionStart);

        var production = helper[productionStart..localStart];
        Assert.Contains("certbot certonly --webroot --webroot-path", production, StringComparison.Ordinal);
        Assert.Contains("certbot ACME validation failed", production, StringComparison.Ordinal);
        Assert.Contains("exit 1", production, StringComparison.Ordinal);
        Assert.DoesNotContain("certonly --apache", production, StringComparison.Ordinal);
        Assert.DoesNotContain("openssl req -x509", production, StringComparison.Ordinal);
        Assert.DoesNotContain("self-signed", production, StringComparison.OrdinalIgnoreCase);

        var local = helper[localStart..];
        Assert.Contains("if [ \"$mode\" = local ]", helper, StringComparison.Ordinal);
        Assert.Contains("ACME is not contacted", helper, StringComparison.Ordinal);
        Assert.Contains("openssl req -x509", local, StringComparison.Ordinal);
        Assert.Contains("local self-signed HTTPS", local, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HistoricalDeployRequiresBackupAndReadiness()
    {
        var script = Read("deploy", "scripts", "deploy.sh");
        Assert.Contains("pg_dump FAILED - refusing to deploy", script, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:${PORT_BACK}/health/ready", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pg_dump FAILED - no pre-migration backup", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GitHubWorkflowUsesMinimumPermissions_AndPinnedSdk()
    {
        var build = Read(".github", "workflows", "build-test.yml");
        var parsed = new DeserializerBuilder().Build().Deserialize<object>(build);

        Assert.NotNull(parsed);
        Assert.Contains("permissions:\n  contents: read", build.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.x", build, StringComparison.Ordinal);
        Assert.Contains("dotnet-version: '10.0.202'", build, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanCloneBuildsProvideRequiredDatabaseSettingsBeforeStartup()
    {
        var testFactory = Read(
            "tests",
            "Aetheus.Back.Tests",
            "Shared",
            "CustomWebApplicationFactory.cs");
        Assert.Contains("builder.UseEnvironment(\"Development\")", testFactory, StringComparison.Ordinal);
        Assert.Contains("builder.UseSetting(\n            \"ConnectionStrings:Default\"", testFactory.Replace("\r\n", "\n"), StringComparison.Ordinal);

        var dockerfile = Read("deploy", "docker", "Dockerfile.back");
        var designConnection = dockerfile.IndexOf("ENV AETHEUS_DESIGN_CONNECTION=", StringComparison.Ordinal);
        var migrationBundle = dockerfile.IndexOf("dotnet-ef migrations bundle", StringComparison.Ordinal);
        Assert.True(designConnection >= 0, "The EF child process requires a build-stage design-time connection.");
        Assert.True(designConnection < migrationBundle, "The design-time connection must precede the EF bundle command.");
    }

    [Fact]
    public void BuildSdkAndRemoteHostTrustArePinned()
    {
        var globalJson = Read("global.json");
        Assert.Contains("\"version\": \"10.0.202\"", globalJson, StringComparison.Ordinal);
        Assert.Contains("\"rollForward\": \"disable\"", globalJson, StringComparison.Ordinal);

        var linuxInstaller = Read("deploy", "scripts", "install-agent-linux.sh");
        Assert.Contains("DOTNET_SDK_VERSION=\"10.0.202\"", linuxInstaller, StringComparison.Ordinal);
        Assert.Contains("--version \"$DOTNET_SDK_VERSION\"", linuxInstaller, StringComparison.Ordinal);
        Assert.DoesNotContain("--channel \"$DOTNET_CHANNEL\" --install-dir /usr/share/dotnet", linuxInstaller, StringComparison.Ordinal);

        var sdkBootstrap = Read("deploy", "scripts", "ensure-dotnet-sdk.sh");
        Assert.Contains("SDK_VERSION=\"$(sed", sdkBootstrap, StringComparison.Ordinal);
        Assert.Contains("${AETHEUS_DOTNET_ROOT:-$HOME/.aetheus/dotnet}", sdkBootstrap, StringComparison.Ordinal);
        Assert.Contains("--version \"$SDK_VERSION\"", sdkBootstrap, StringComparison.Ordinal);
        Assert.Contains("Pinned .NET SDK $SDK_VERSION installation could not be verified", sdkBootstrap, StringComparison.Ordinal);

        var deploymentFiles = Directory.EnumerateFiles(Path.Combine(Root, "deploy", "pipelines"), "*.yaml")
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "deploy", "docker"), "Dockerfile*"));
        foreach (var path in deploymentFiles)
        {
            var content = File.ReadAllText(path);
            Assert.DoesNotContain("StrictHostKeyChecking=no", content, StringComparison.Ordinal);
            Assert.DoesNotContain("UserKnownHostsFile=/dev/null", content, StringComparison.Ordinal);
            Assert.DoesNotContain("--channel 10.0", content, StringComparison.Ordinal);
            Assert.DoesNotContain("10.0.x", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BackendComposeFilesUseThePersistentAppOwnedDataProtectionDirectory()
    {
        const string keyPath = "DataProtection__KeyPath=/app/data/dp-keys";
        foreach (var composeFile in new[]
                 {
                     "local.compose.yml",
                     "remote.compose.yml",
                     "remote-bluegreen.compose.yml"
                 })
        {
            var compose = Read("deploy", "compose", composeFile);
            Assert.Contains(keyPath, compose, StringComparison.Ordinal);
            Assert.Contains("dp-keys:/app/data/dp-keys", compose, StringComparison.Ordinal);
        }

        var dockerfile = Read("deploy", "docker", "Dockerfile.back");
        Assert.Contains("/app/data/dp-keys", dockerfile, StringComparison.Ordinal);
        Assert.Contains("chown -R app:app /app/logs /app/data", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void VpsSimulatorPersistsAgentStateAlongsideNestedDockerState()
    {
        var compose = Read("deploy", "compose", "vpssim.compose.yml");

        Assert.Contains("vpssim-docker:/var/lib/docker", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-containerd:/var/lib/containerd", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-agent-state:/var/lib/aetheus-agent", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-apache:/etc/apache2", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-letsencrypt:/etc/letsencrypt", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-www:/var/www", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-agent-state:", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("down -v", compose, StringComparison.Ordinal);

        var bootstrap = Read("deploy", "docker", "vpssim-agent-bootstrap.sh");
        Assert.Contains("preserve_deploy_state", bootstrap, StringComparison.Ordinal);
        Assert.Contains("restore_deploy_state", bootstrap, StringComparison.Ordinal);
        Assert.Contains("/var/lib/aetheus-agent/aetheus-*", bootstrap, StringComparison.Ordinal);
        Assert.Contains("trap restore_deploy_state EXIT", bootstrap, StringComparison.Ordinal);
        Assert.Contains("^https://(host\\.docker\\.internal|localhost|127\\.0\\.0\\.1)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("set AGENT_TOKEN or VPSSIM_ADMIN_PASSWORD", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("ADMIN_PASSWORD=\"${ADMIN_PASSWORD:-aetheus-dev-admin-pwd}\"", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("VPSSIM_ADMIN_PASSWORD:-aetheus-dev-admin-pwd", compose, StringComparison.Ordinal);
        Assert.True(
            bootstrap.IndexOf("preserve_deploy_state\n", StringComparison.Ordinal)
            < bootstrap.IndexOf("./install-agent-linux.sh", StringComparison.Ordinal));
        Assert.True(
            bootstrap.IndexOf("restore_deploy_state\n", bootstrap.IndexOf("./install-agent-linux.sh", StringComparison.Ordinal), StringComparison.Ordinal)
            > bootstrap.IndexOf("./install-agent-linux.sh", StringComparison.Ordinal));

        var vpsSimulator = Read("deploy", "docker", "Dockerfile.vpssim");
        Assert.Contains(
            "a2enmod rewrite headers ssl proxy proxy_http proxy_wstunnel",
            vpsSimulator,
            StringComparison.Ordinal);
        Assert.Contains("/etc/letsencrypt/options-ssl-apache.conf", vpsSimulator, StringComparison.Ordinal);
        Assert.Contains("SSLProtocol all -SSLv2 -SSLv3 -TLSv1 -TLSv1.1", vpsSimulator, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductCoverageIncludesEveryHandwrittenSourceFile()
    {
        var settings = Read("coverage.runsettings");
        Assert.DoesNotContain("**/Program.cs", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoGeneratedProgram", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("DesignTimeDbContextFactory", settings, StringComparison.Ordinal);

        var logger = Read("src", "Aetheus.Back", "Middleware", "JsonFileLoggerProvider.cs");
        Assert.DoesNotContain("ExcludeFromCodeCoverage", logger, StringComparison.Ordinal);
        Assert.Contains("DroppedEntries", logger, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
