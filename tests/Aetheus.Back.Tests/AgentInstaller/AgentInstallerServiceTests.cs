// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AgentInstaller;
using Aetheus.Back.Exceptions;

namespace Aetheus.Back.Tests.AgentInstaller;

public class AgentInstallerServiceTests
{
    private readonly AgentInstallerService _svc = new();

    [Fact]
    public void Build_Linux_PipelineRunnerDefault_DelegatesToInstallerWithToolchain()
    {
        var script = _svc.Build("linux", "tok", "https://aetheus.example.com", "1.0.0");

        // The one-shot delegates to the bundled full installer, which provisions Git workspace
        // preparation via its default-on pipeline-runner module - so the script invokes the installer
        // and must NOT opt out of pipeline-runner.
        Assert.Contains("install-agent-linux.sh", script.Body);
        Assert.DoesNotContain("--no-pipeline-runner", script.Body);
    }

    [Fact]
    public void Build_Linux_PipelineRunnerOff_OptsOutOfToolchain()
    {
        var script = _svc.Build("linux", "tok", "https://aetheus.example.com", "1.0.0", pipelineRunner: false);

        Assert.Contains("install-agent-linux.sh", script.Body);
        Assert.Contains("--no-pipeline-runner", script.Body);
    }

    [Fact]
    public void Build_Linux_ServerManagement_PassesModuleFlag()
    {
        var script = _svc.Build("linux", "tok", "https://aetheus.example.com", "1.0.0", serverManagement: true);

        // Server-management is now genuinely provisioned by delegating to the full installer with the flag.
        Assert.Contains("install-agent-linux.sh", script.Body);
        Assert.Contains("--module server-management", script.Body);
    }

    [Fact]
    public void Build_Windows_PipelineRunnerDefault_EmbedsToolchain()
    {
        var script = _svc.Build("windows", "tok", "https://aetheus.example.com", "1.0.0");

        Assert.Contains("Git.Git", script.Body);
        Assert.DoesNotContain("Microsoft.DotNet.SDK", script.Body);
    }

    [Fact]
    public void Build_Linux_UsesTheArchiveRouteServedByTheBackend()
    {
        var script = _svc.Build("linux", "tok", "https://aetheus.example.com", "1.0.0");

        Assert.Contains("${SERVER_URL}/downloads/aetheus-agent-linux-x64.tar.gz", script.Body);
        Assert.DoesNotContain("aetheus-agent-linux-1.0.0.tar.gz", script.Body);
    }

    [Fact]
    public void Build_Windows_UsesTheArchiveRouteServedByTheBackend()
    {
        var script = _svc.Build("windows", "tok", "https://aetheus.example.com", "1.0.0");

        Assert.Contains("$ServerUrl/downloads/aetheus-agent-win-x64.zip", script.Body);
        Assert.DoesNotContain("aetheus-agent-windows-1.0.0.zip", script.Body);
    }

    [Theory]
    [InlineData("linux")]
    [InlineData("windows")]
    public void Build_InterpolatesTokenUrlAndVersion(string platform)
    {
        // The generated script must actually carry the caller's token/url/version - assert on unique
        // sentinel values so a regression that drops or mis-embeds them is caught (not just toolchain presence).
        var script = _svc.Build(platform, "TOKEN-SENTINEL-123", "https://host.example.org", "9.9.9-rc");

        Assert.Contains("TOKEN-SENTINEL-123", script.Body);
        Assert.Contains("https://host.example.org", script.Body);
        Assert.Contains("9.9.9-rc", script.Body);
    }

    [Fact]
    public void Build_UnknownPlatform_ThrowsBadRequest()
    {
        Assert.Throws<BadRequestException>(
            () => _svc.Build("solaris", "tok", "https://aetheus.example.com", "1.0.0"));
    }

    [Theory]
    [InlineData("tok en")]              // space
    [InlineData("tok\"quote")]          // double quote (would break bash embedding)
    [InlineData("tok'quote")]           // single quote (would break PowerShell embedding)
    [InlineData("tok`backtick")]        // backtick
    [InlineData("tok$injection")]       // shell expansion char
    public void Build_TokenWithUnsafeChars_ThrowsBadRequest(string token)
    {
        Assert.Throws<BadRequestException>(
            () => _svc.Build("linux", token, "https://aetheus.example.com", "1.0.0"));
    }

    [Theory]
    [InlineData("AbC123+/=")]           // base64 charset
    [InlineData("token-with_base64url")] // base64url charset
    public void Build_TokenWithSafeCharset_Succeeds(string token)
    {
        var script = _svc.Build("linux", token, "https://aetheus.example.com", "1.0.0");
        Assert.Contains(token, script.Body);
    }
}
