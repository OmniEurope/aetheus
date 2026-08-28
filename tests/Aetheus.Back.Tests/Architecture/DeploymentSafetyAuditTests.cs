// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Architecture;

public sealed class DeploymentSafetyAuditTests
{
    private static string Root => FindRepoRoot();
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [Fact]
    public void AgentReinstall_PreservesElevationPosture_AndNeverSilentlyRevokesCertbot()
    {
        var installer = Read("deploy", "scripts", "install-agent-linux.sh");
        var behavioralProof = Read("deploy", "scripts", "verify-agent-posture-idempotence.sh");

        // apply_sudoers revokes (rm -f) every capability whose flag is 0, so the default install
        // mode MUST derive the existing posture, and it must do so before the cleanup deletes the
        // sudoers files that are the only on-disk witnesses of that posture.
        var cleanup = installer.IndexOf("# --- Clean any previous installation ---", StringComparison.Ordinal);
        Assert.True(cleanup > 0);
        var installDerivation = installer.LastIndexOf("derive_existing_posture", cleanup, StringComparison.Ordinal);
        Assert.True(installDerivation >= 0, "The default install mode must derive the existing posture.");
        Assert.True(installDerivation < cleanup);

        // certbot-manage was the one capability missing from the derivation, so every reinstall
        // silently revoked HTTPS issuance and the production deployment failed on certbot.
        var derivation = installer.IndexOf("derive_existing_posture() {", StringComparison.Ordinal);
        var derivationEnd = installer.IndexOf("write_sudoers() {", derivation, StringComparison.Ordinal);
        Assert.True(derivation > 0 && derivationEnd > derivation);
        var derivationBody = installer[derivation..derivationEnd];
        foreach (var witness in new[]
                 {
                     "SUDOERS_FILE", "APACHE_MANAGE_SUDOERS_FILE", "CERTBOT_MANAGE_SUDOERS_FILE",
                     "RKHUNTER_MANAGE_SUDOERS_FILE", "CRON_MANAGE_SUDOERS_FILE",
                     "PORTSENTRY_MANAGE_SUDOERS_FILE", "SERVICE_ENABLE_SUDOERS_FILE",
                     "PACKAGE_MANAGE_SUDOERS_FILE", "PATCH_MANAGE_SUDOERS_FILE",
                     "FIREWALL_MANAGE_SUDOERS_FILE", "MAIL_MANAGE_SUDOERS_FILE",
                     "TEAMSPEAK_SETUP_SUDOERS_FILE", "DEPLOY_MANAGE_SUDOERS_FILE",
                 })
            Assert.Contains(witness, derivationBody, StringComparison.Ordinal);

        // An explicit --enable-*/--disable-* still wins, otherwise a revocation could never be applied.
        Assert.Contains("POSTURE_EXPLICIT", derivationBody, StringComparison.Ordinal);

        // The agent publishes its ServerUrl as the package base URL and NuGet refuses plain HTTP.
        Assert.Contains("NuGet requires HTTPS sources", installer, StringComparison.Ordinal);
        Assert.Contains("SERVER_URL_HTTPS=", installer, StringComparison.Ordinal);

        Assert.Contains("derive_existing_posture", behavioralProof, StringComparison.Ordinal);
        Assert.Contains("Reinstall would have revoked", behavioralProof, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentInstaller_InstallsVersionedAutonomousFullUpgradeSupervisor()
    {
        var installer = Read("deploy", "scripts", "install-agent-linux.sh");
        var executor = Read(
            "src", "Aetheus.Agent.Core", "Operations", "AgentSelfUpdateOperationExecutor.cs");

        Assert.Contains("AGENT_POSTURE_VERSION=\"2\"", installer, StringComparison.Ordinal);
        Assert.Contains(
            "AGENT_POSTURE_VERSION_FILE=\"/etc/aetheus-agent-posture-version\"",
            installer,
            StringComparison.Ordinal);
        Assert.Contains(
            "PathExists=$AGENT_UPDATE_REQUEST_FILE",
            installer,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExecStart=$AGENT_UPDATE_WORKER_PATH",
            installer,
            StringComparison.Ordinal);
        Assert.Contains(
            "$server_url/downloads/releases/$target_version/$archive_name",
            installer,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "$server_url/downloads/aetheus-agent-linux-x64.tar.gz",
            installer,
            StringComparison.Ordinal);
        Assert.Contains("mv -- \"$request_file\" \"$running_file\"", installer, StringComparison.Ordinal);
        Assert.Contains(".agent-posture-upgrade-failed-request", installer, StringComparison.Ordinal);
        Assert.Contains(".agent-posture-upgrade-result", installer, StringComparison.Ordinal);
        Assert.Contains("--retry 5 --retry-all-errors", installer, StringComparison.Ordinal);
        Assert.Contains("stat -c %s \"$archive\"", installer, StringComparison.Ordinal);
        Assert.Contains("sha256sum \"$archive\"", installer, StringComparison.Ordinal);
        Assert.Contains("systemctl is-active --quiet aetheus-agent", installer, StringComparison.Ordinal);
        Assert.True(
            installer.IndexOf("systemctl is-active --quiet aetheus-agent", StringComparison.Ordinal)
            < installer.IndexOf("outcome=\"success\"", StringComparison.Ordinal));
        Assert.Contains(
            "sh \"$payload/install-agent-linux.sh\" --upgrade --yes",
            installer,
            StringComparison.Ordinal);
        var upgradeMode = installer[installer.IndexOf("# Mode: UPGRADE", StringComparison.Ordinal)..installer.IndexOf("# Mode: INSTALL", StringComparison.Ordinal)];
        Assert.DoesNotContain(
            "preserve_production_environment",
            upgradeMode,
            StringComparison.Ordinal);
        Assert.Contains(
            "cp -a \"$rollback\"/. \"$install_dir\"/",
            installer,
            StringComparison.Ordinal);
        Assert.Equal(
            3,
            installer.Split("install_agent_update_supervisor \"", StringSplitOptions.None).Length - 1);
        Assert.Contains(
            "LinuxIntegrationPostureVersionPath",
            executor,
            StringComparison.Ordinal);
        Assert.Contains(
            ".agent-posture-upgrade-request",
            executor,
            StringComparison.Ordinal);
        Assert.Contains("sleep 5", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("sleep 150", installer, StringComparison.Ordinal);
        var supervisorInstaller = installer[
            installer.IndexOf("install_agent_update_supervisor()", StringComparison.Ordinal)..];
        supervisorInstaller = supervisorInstaller[..supervisorInstaller.IndexOf("ensure_docker_group()", StringComparison.Ordinal)];
        Assert.DoesNotContain("rm -f -- \"$AGENT_UPDATE_REQUEST_FILE\"", supervisorInstaller, StringComparison.Ordinal);
        Assert.DoesNotContain("sudo -n", executor, StringComparison.Ordinal);
    }

    /// <summary>
    /// Secret-zero and the production state directory. These moved out of the pipeline and into the
    /// preparation script when the cutover became typed, but not one of the checks may move with
    /// them: regenerating a secret over persistent state produces a database nothing can open, and it
    /// cannot be undone afterwards.
    /// </summary>
    [Fact]
    public void CanonicalDeployBootstrapsProductionStateWithoutExposingExistingSecrets()
    {
        var prepare = Read("deploy", "scripts", "prod-deploy-prepare.sh");

        Assert.Contains("STATE_DIR\" = /var/lib/aetheus-production", prepare, StringComparison.Ordinal);
        Assert.Contains("stat -c %u /var/lib/aetheus-agent", prepare, StringComparison.Ordinal);
        Assert.Contains("stat -c %g /var/lib/aetheus-agent", prepare, StringComparison.Ordinal);
        Assert.Contains("Refusing to remap a non-empty production state directory", prepare, StringComparison.Ordinal);
        Assert.Contains("--network none --read-only --user 0:0", prepare, StringComparison.Ordinal);
        Assert.Contains("--cap-drop ALL --cap-add CHOWN --cap-add FOWNER", prepare, StringComparison.Ordinal);
        Assert.Contains("--security-opt no-new-privileges --pids-limit 16", prepare, StringComparison.Ordinal);
        Assert.Contains("--volume \"$STATE_DIR:/state\"", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("--volume /var/lib:/", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("--privileged", prepare, StringComparison.Ordinal);
        Assert.Contains("stat -c %a \"$STATE_DIR\")\" = 700", prepare, StringComparison.Ordinal);
        // The privileged bootstrap runs from the immutable artifact, so the image has to be loaded
        // and verified first. Ordering, not mere presence: an unverified image would be root here.
        Assert.True(
            prepare.IndexOf("docker load", StringComparison.Ordinal)
            < prepare.IndexOf("STATE_BOOTSTRAP_REQUIRED=false", StringComparison.Ordinal));
        Assert.True(
            prepare.IndexOf("Image revision is invalid", StringComparison.Ordinal)
            < prepare.IndexOf("STATE_BOOTSTRAP_REQUIRED=false", StringComparison.Ordinal));
        Assert.True(
            prepare.IndexOf("STATE_BOOTSTRAP_REQUIRED=false", StringComparison.Ordinal)
            < prepare.IndexOf("PRODUCTION_STATE_PRESENT=true", StringComparison.Ordinal));
        Assert.True(
            prepare.IndexOf("PRODUCTION_STATE_PRESENT=true", StringComparison.Ordinal)
            < prepare.IndexOf("openssl rand -hex 24", StringComparison.Ordinal));
        Assert.Contains("Persistent production state exists but", prepare, StringComparison.Ordinal);
        Assert.Contains("Secret regeneration is forbidden", prepare, StringComparison.Ordinal);
        Assert.Contains("Reusing existing secrets", prepare, StringComparison.Ordinal);
        // The preparation must not deploy: a second cutover implementation is exactly the drift the
        // shared template exists to prevent.
        foreach (var cutoverOperation in new[]
                 { "docker compose", "systemctl reload", "aetheus-apache-reload", "pg_dump" })
        {
            Assert.DoesNotContain(cutoverOperation, prepare, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductionImageRetention_IsApplicationScopedAndProtectsEveryContainerImage()
    {
        var script = Read("deploy", "scripts", "retain-docker-images.sh");
        Assert.Contains("docker ps -aq --no-trunc", script, StringComparison.Ordinal);
        Assert.Contains("flock -n 9", script, StringComparison.Ordinal);
        Assert.Contains("refresh_protected_images", script, StringComparison.Ordinal);
        Assert.Contains("docker image inspect --format '{{.Id}}' \"$tag\"", script, StringComparison.Ordinal);
        Assert.Contains("refusing stale deletion", script, StringComparison.Ordinal);
        Assert.Contains("docker inspect --format '{{.Image}}'", script, StringComparison.Ordinal);
        Assert.Contains("SKIP vanished container", script, StringComparison.Ordinal);
        Assert.Contains("--filter \"id=$container\"", script, StringComparison.Ordinal);
        Assert.Contains("refusing image retention", script, StringComparison.Ordinal);
        Assert.DoesNotContain("docker inspect --format '{{.Image}}' \"$container\" 2>/dev/null || true", script, StringComparison.Ordinal);
        Assert.Contains("grep -Fxq \"$image_id\" \"$PROTECTED\"", script, StringComparison.Ordinal);
        Assert.Contains("AETHEUS_IMAGE_RETENTION_DRY_RUN", script, StringComparison.Ordinal);
        Assert.Contains("AETHEUS_IMAGE_RETENTION_DRY_RUN:-false", script, StringComparison.Ordinal);
        Assert.Contains("docker image rm \"$tag\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("docker system prune", script, StringComparison.Ordinal);
        Assert.DoesNotContain("docker image prune", script, StringComparison.Ordinal);
        Assert.DoesNotContain("docker volume", script, StringComparison.Ordinal);
        Assert.DoesNotContain("docker image rm -f", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallersFailClosed_AndWindowsDoesNotUseLocalSystem()
    {
        var windows = Read("deploy", "scripts", "install-agent-windows.ps1");
        Assert.Contains("NT SERVICE\\$ServiceName", windows, StringComparison.Ordinal);
        Assert.Contains("Agent enrollment could not be proven", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("registration could not be confirmed from logs", windows, StringComparison.Ordinal);
        Assert.Contains("DockerStorageMaintenance", windows, StringComparison.Ordinal);
        Assert.Contains("PolicyVersion = 3", windows, StringComparison.Ordinal);
        Assert.Contains("DryRun = $false", windows, StringComparison.Ordinal);
        Assert.Contains("DeploymentOnly = $false", windows, StringComparison.Ordinal);
        Assert.Contains("AllowBuildsOnDeploymentTarget = $false", windows, StringComparison.Ordinal);
        Assert.Contains("ReservedSpaceGiB = 5", windows, StringComparison.Ordinal);
        Assert.Contains("MaxCacheGiB = 15", windows, StringComparison.Ordinal);
        Assert.Contains("${serviceIdentity}:(OI)(CI)M", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("${serviceIdentity}:(OI)(CI)RX", windows, StringComparison.Ordinal);

        var linux = Read("deploy", "scripts", "install-agent-linux.sh");
        Assert.Contains("No successful enrollment outcome was observed", linux, StringComparison.Ordinal);
        Assert.Contains("rm -f \"$CERTBOT_MANAGE_SUDOERS_FILE\" \"$TEAMSPEAK_SETUP_SUDOERS_FILE\"", linux, StringComparison.Ordinal);
        Assert.Contains("gpasswd -d \"$AGENT_USER\" docker", linux, StringComparison.Ordinal);
        Assert.Contains("gpasswd -d \"$AGENT_USER\" teamspeak", linux, StringComparison.Ordinal);
        Assert.Contains("\"DockerStorageMaintenance\"", linux, StringComparison.Ordinal);
        Assert.Contains("\"PolicyVersion\": 3", linux, StringComparison.Ordinal);
        Assert.Contains("\"DryRun\": false", linux, StringComparison.Ordinal);
        Assert.Contains(
            "if [ \"$MODULE_DEPLOYMENT\" -eq 1 ] && [ \"$MODULE_PIPELINE_RUNNER\" -eq 0 ]; then",
            linux,
            StringComparison.Ordinal);
        Assert.Contains("DOCKER_STORAGE_DEPLOYMENT_ONLY=true", linux, StringComparison.Ordinal);
        Assert.Contains("\"AllowBuildsOnDeploymentTarget\": false", linux, StringComparison.Ordinal);
        Assert.Contains("\"ReservedSpaceGiB\": 5", linux, StringComparison.Ordinal);
        Assert.Contains("\"MaxCacheGiB\": 15", linux, StringComparison.Ordinal);
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
    public void ApacheConfigTest_UsesFixedRootOwnedHelperWithoutNoexec()
    {
        var installer = Read("deploy", "scripts", "install-agent-linux.sh");
        var blockStart = installer.IndexOf("Cmnd_Alias AETHEUS_APACHE_TEST", StringComparison.Ordinal);
        var blockEnd = installer.IndexOf("visudo -cf \"$APACHE_MANAGE_SUDOERS_FILE\"", blockStart, StringComparison.Ordinal);
        Assert.True(blockStart >= 0 && blockEnd > blockStart);
        var sudoersBlock = installer[blockStart..blockEnd];

        Assert.Contains("exec /usr/sbin/apache2ctl configtest", installer, StringComparison.Ordinal);
        Assert.Contains("chown root:root \"$APACHE_CONFIGTEST_HELPER_PATH\"", installer, StringComparison.Ordinal);
        Assert.Contains("chmod 755 \"$APACHE_CONFIGTEST_HELPER_PATH\"", installer, StringComparison.Ordinal);
        Assert.Contains("Cmnd_Alias AETHEUS_APACHE_TEST = $APACHE_CONFIGTEST_HELPER_PATH", sudoersBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("Defaults!AETHEUS_APACHE_TEST noexec", sudoersBlock, StringComparison.Ordinal);
        Assert.Contains("Defaults!AETHEUS_APACHE_MANAGE noexec", sudoersBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("/usr/sbin/apache2ctl configtest", sudoersBlock, StringComparison.Ordinal);
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
        var migrationBundle = dockerfile.IndexOf("dotnet-ef migrations bundle", StringComparison.Ordinal);
        Assert.True(migrationBundle >= 0, "The backend image must build an EF migration bundle.");
        var designConnection = dockerfile.IndexOf("--design-connection", migrationBundle, StringComparison.Ordinal);
        Assert.True(designConnection > migrationBundle, "The EF bundle command must pass its design-time connection explicitly.");
        Assert.DoesNotContain("ENV AETHEUS_DESIGN_CONNECTION=", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalObservabilityVerification_UsesTheRepositoryNuGetConfigWithExactCase()
    {
        var script = Read("deploy", "scripts", "verify-optional-observability.sh");
        var variantStart = script.IndexOf("verify_variant()", StringComparison.Ordinal);
        var variantBlock = script[variantStart..];

        Assert.Contains("--configfile \"$ROOT/NuGet.config\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("NuGet.Config", script, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Root, "NuGet.config")));
        Assert.Equal(2, variantBlock.Split("\"$DOTNET\" restore \"$PROJECT\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("--use-lock-file --force-evaluate", variantBlock, StringComparison.Ordinal);
        Assert.Contains("-p:RestoreLockedMode=false", variantBlock, StringComparison.Ordinal);
        Assert.Contains("--locked-mode", variantBlock, StringComparison.Ordinal);
        Assert.Contains("generated_lock_hash=", variantBlock, StringComparison.Ordinal);
        Assert.Contains("test \"$generated_lock_hash\" = \"$locked_lock_hash\"", variantBlock, StringComparison.Ordinal);
        foreach (var nonPortableLock in new[]
                 {
                     "packages.telemetry.lock.json",
                     "packages.web-analytics.lock.json",
                     "packages.both.lock.json"
                 })
        {
            Assert.False(File.Exists(Path.Combine(Root, "examples", "optional-observability", nonPortableLock)));
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
        var dockerfile = Read("deploy", "docker", "Dockerfile.vpssim");

        Assert.Contains("vpssim-docker:/var/lib/docker", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-containerd:/var/lib/containerd", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-agent-state:/var/lib/aetheus-agent", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-apache:/etc/apache2", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-letsencrypt:/etc/letsencrypt", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-www:/var/www", compose, StringComparison.Ordinal);
        Assert.Contains("vpssim-agent-state:", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("down -v", compose, StringComparison.Ordinal);

        Assert.DoesNotContain("nodejs.org", dockerfile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("node --version", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("npm --version", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("ARG NODE_VERSION", dockerfile, StringComparison.Ordinal);

        var bootstrap = Read("deploy", "docker", "vpssim-agent-bootstrap.sh");
        Assert.Contains("preserve_deploy_state", bootstrap, StringComparison.Ordinal);
        Assert.Contains("restore_deploy_state", bootstrap, StringComparison.Ordinal);
        Assert.Contains("/var/lib/aetheus-agent/aetheus-*", bootstrap, StringComparison.Ordinal);
        Assert.Contains("trap restore_deploy_state EXIT", bootstrap, StringComparison.Ordinal);
        Assert.Contains("[ -x /opt/aetheus-agent/Aetheus.Agent.Linux ]", bootstrap, StringComparison.Ordinal);
        Assert.Contains("rm -f /var/lib/vpssim-agent-installed", bootstrap, StringComparison.Ordinal);
        Assert.Contains("^https://(host\\.docker\\.internal|localhost|127\\.0\\.0\\.1)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("set AGENT_TOKEN or VPSSIM_ADMIN_PASSWORD", bootstrap, StringComparison.Ordinal);
        Assert.Contains("AGENT_TOKEN=${VPSSIM_AGENT_TOKEN:-}", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("ADMIN_PASSWORD=\"${ADMIN_PASSWORD:-aetheus-dev-admin-pwd}\"", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("VPSSIM_ADMIN_PASSWORD:-aetheus-dev-admin-pwd", compose, StringComparison.Ordinal);
        Assert.Contains("set -eu", bootstrap, StringComparison.Ordinal);
        Assert.Contains("if ./install-agent-linux.sh", bootstrap, StringComparison.Ordinal);
        Assert.Contains("RC=$?", bootstrap, StringComparison.Ordinal);
        Assert.True(
            bootstrap.IndexOf("preserve_deploy_state\n", StringComparison.Ordinal)
            < bootstrap.IndexOf("./install-agent-linux.sh", StringComparison.Ordinal));
        Assert.True(
            bootstrap.IndexOf("restore_deploy_state\n", bootstrap.IndexOf("./install-agent-linux.sh", StringComparison.Ordinal), StringComparison.Ordinal)
            > bootstrap.IndexOf("./install-agent-linux.sh", StringComparison.Ordinal));

        var launcher = Read("scripts", "launch-core.ps1");
        Assert.Contains("GitLight__CloneBaseUrl", launcher, StringComparison.Ordinal);
        Assert.Contains("src/Aetheus.Back/appsettings.Development.json", launcher, StringComparison.Ordinal);
        Assert.Contains("[string]$developmentAuth.AdminUser", launcher, StringComparison.Ordinal);
        Assert.Contains("[string]$developmentAuth.AdminPassword", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("else { \"admin\" }", launcher, StringComparison.Ordinal);

        var ensureBuildx = Read("deploy", "scripts", "ensure-buildx-builder.sh");
        Assert.Contains("docker buildx inspect \"$BUILDER\" --bootstrap", ensureBuildx, StringComparison.Ordinal);
        Assert.Contains("docker buildx rm --force \"$BUILDER\"", ensureBuildx, StringComparison.Ordinal);
        Assert.Contains("--driver docker-container", ensureBuildx, StringComparison.Ordinal);
        Assert.Contains("--bootstrap >/dev/null", ensureBuildx, StringComparison.Ordinal);
        var vpsSimulator = Read("deploy", "docker", "Dockerfile.vpssim");
        Assert.Contains(
            "a2enmod rewrite headers ssl proxy proxy_http proxy_wstunnel",
            vpsSimulator,
            StringComparison.Ordinal);
        Assert.Contains("/etc/letsencrypt/options-ssl-apache.conf", vpsSimulator, StringComparison.Ordinal);
        Assert.Contains("SSLProtocol all -SSLv2 -SSLv3 -TLSv1 -TLSv1.1", vpsSimulator, StringComparison.Ordinal);
        Assert.Contains(
            "775a5731a9809801e4c8f9066cd9bc562a1b368553139c1249f2a0740d50041e  /tmp/ts3.tar.bz2\" | sha256sum -c -",
            vpsSimulator,
            StringComparison.Ordinal);

        var provisioning = Read("deploy", "docker", "vpssim-provision.sh");
        Assert.Contains("set -eu", provisioning, StringComparison.Ordinal);

        var publishVitrine = Read("deploy", "scripts", "publish-vitrine.sh");
        Assert.Contains("/var/www/aetheus-*", publishVitrine, StringComparison.Ordinal);
        Assert.Contains("readlink -m -- \"$WEB_ROOT\"", publishVitrine, StringComparison.Ordinal);
        Assert.Contains("Refusing publication through a symbolic-link web root", publishVitrine, StringComparison.Ordinal);
        Assert.Contains("Refusing to publish a vitrine containing unresolved", publishVitrine, StringComparison.Ordinal);
        Assert.DoesNotContain("grep -rc \"aetheus\\.example\" site/ || true", publishVitrine, StringComparison.Ordinal);
    }

    [Fact]
    public void ApacheDefaultVhostRejectsUnknownHttpHostsAndSniNames()
    {
        var fallbackPath = Path.Combine(Root, "deploy", "apache", "000-default.conf");
        var applicationPath = Path.Combine(Root, "deploy", "apache", "aetheus.conf");
        var fallback = File.ReadAllText(fallbackPath);

        Assert.True(
            StringComparer.Ordinal.Compare(Path.GetFileName(fallbackPath), Path.GetFileName(applicationPath)) < 0,
            "The neutral fallback must sort before application vhosts.");
        Assert.Contains("<VirtualHost *:80>", fallback, StringComparison.Ordinal);
        Assert.Contains("<VirtualHost *:443>", fallback, StringComparison.Ordinal);
        Assert.Equal(2, fallback.Split("Require all denied", StringSplitOptions.None).Length - 1);
        Assert.Contains("SSLEngine on", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("ProxyPass", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentRoot", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("aetheus-api.sonytumen.com", fallback, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsLauncherReportsBackgroundDotnetFailuresWithoutGuiDialog()
    {
        var launcher = Read("scripts", "launch-core.ps1");
        const string guardedStart =
            "Start-Job -InitializationScript $dotnetJobInitialization -ScriptBlock";

        Assert.Contains("$env:DOTNET_DISABLE_GUI_ERRORS = \"1\"", launcher, StringComparison.Ordinal);
        Assert.Equal(
            launcher.Split("Start-Job", StringSplitOptions.None).Length - 1,
            launcher.Split(guardedStart, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("Start-Job -ScriptBlock", launcher, StringComparison.Ordinal);
        Assert.Equal(
            launcher.Split(guardedStart, StringSplitOptions.None).Length - 1,
            System.Text.RegularExpressions.Regex.Matches(
                launcher,
                "(?m)^\\s*dotnet (?:watch )?run .+ 2>&1; \\\"##PROMEXIT##\\$LASTEXITCODE\\\"\\r?$").Count);
        Assert.Contains("^##PROMEXIT##(-?\\d+)$", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("^##PROMEXIT##(\\d+)$", launcher, StringComparison.Ordinal);
        Assert.Contains("$backReady = Wait-ForEndpoint -url $backUrl", launcher, StringComparison.Ordinal);
        Assert.Contains("Server startup failed. Dumping captured output", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductCoverageIncludesEveryHandwrittenSourceFile()
    {
        var settings = Read("coverage.runsettings");
        Assert.Contains("**/obj/**/*.cs", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("**/Program.cs", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoGeneratedProgram", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("DesignTimeDbContextFactory", settings, StringComparison.Ordinal);

        var logger = Read("src", "Aetheus.Back", "Middleware", "JsonFileLoggerProvider.cs");
        Assert.DoesNotContain("ExcludeFromCodeCoverage", logger, StringComparison.Ordinal);
        Assert.Contains("DroppedEntries", logger, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configuration key the deployment REQUIRES must be one the deployment PROVISIONS. Production
    /// refuses the <c>Auth:EncryptionKey</c> fallback for the bootstrap identity, but nothing wrote
    /// the dedicated key: the environment file is generated once and never rewritten, so every host
    /// installed before that requirement reached its blocking smoke and was refused an identity, with
    /// no route to ever satisfy it. The three halves are pinned together here: the backend reads the
    /// key, Compose passes it, and the preparation script both generates and back-fills it.
    /// </summary>
    [Fact]
    public void TheDeploymentBootstrapIdentityKey_IsProvisioned_NotJustRequired()
    {
        var identity = Read("src", "Aetheus.Back", "Components", "Pipelines", "DeploymentBootstrapIdentity.cs");
        Assert.Contains("Deployment:BootstrapIdentityKey", identity, StringComparison.Ordinal);

        var compose = Read("deploy", "compose", "remote-bluegreen.compose.yml");
        Assert.Contains(
            "Deployment__BootstrapIdentityKey=${DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY}",
            compose,
            StringComparison.Ordinal);

        var prepare = Read("deploy", "scripts", "prod-deploy-prepare.sh");
        // Generated on a first deploy...
        Assert.Contains(
            "DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=$(openssl rand -hex 32)",
            prepare,
            StringComparison.Ordinal);
        // ...back-filled on every host whose environment file predates the requirement...
        Assert.Contains(
            "grep -q \"^DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=.\\+\" \"$ENV_FILE\"",
            prepare,
            StringComparison.Ordinal);
        // ...and verified present before the deployment proceeds.
        Assert.Contains("DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY; do", prepare, StringComparison.Ordinal);

        // The demo host runs the SAME compose file, so the same interpolation applies to it. Only the
        // production path provisioned the key, so on the demo it resolved to an empty string, the
        // deployed backend could not re-derive the throwaway smoke account, and the blocking probe was
        // refused with 401 on a healthy deployment. Nightly run 1204 failed exactly there.
        var demoPrepare = Read("deploy", "scripts", "nightly-demo-prepare.sh");
        Assert.Contains(
            "DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=$(openssl rand -hex 32)",
            demoPrepare,
            StringComparison.Ordinal);
        Assert.Contains(
            "grep -q \"^DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=.\\+\" \"$DEMO_ENV_FILE\"",
            demoPrepare,
            StringComparison.Ordinal);
        Assert.Contains(
            "^DEPLOYMENT_BOOTSTRAP_IDENTITY_KEY=[0-9a-f]{64}$",
            demoPrepare,
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot() => Aetheus.Back.Tests.Architecture.RepositoryScan.Root;
}
