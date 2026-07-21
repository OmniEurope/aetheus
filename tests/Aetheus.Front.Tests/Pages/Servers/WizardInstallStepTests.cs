// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.AgentWizard;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class WizardInstallStepTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    public WizardInstallStepTests() => BunitTestHelper.RegisterServices(this);

    private IRenderedComponent<WizardInstallStep> RenderLinux(
        string baseUrl = "https://example.com",
        string version = "1.0.0",
        string? token = null) =>
        Render<WizardInstallStep>(p => p
            .Add(x => x.Platform, "linux")
            .Add(x => x.ServerBaseUrl, baseUrl)
            .Add(x => x.SiteVersion, version)
            .Add(x => x.InstallerToken, token));

    private IRenderedComponent<WizardInstallStep> RenderWindows(
        string baseUrl = "https://example.com",
        string? token = null) =>
        Render<WizardInstallStep>(p => p
            .Add(x => x.Platform, "windows")
            .Add(x => x.ServerBaseUrl, baseUrl)
            .Add(x => x.SiteVersion, "1.0.0")
            .Add(x => x.InstallerToken, token));

    // === Platform detection ===

    [Fact]
    public void IsLinux_LinuxPlatform_ReturnsTrue()
    {
        var cut = RenderLinux();
        var isLinux = (bool)typeof(WizardInstallStep)
            .GetProperty("IsLinux", Priv)!.GetValue(cut.Instance)!;
        Assert.True(isLinux);
    }

    [Fact]
    public void IsLinux_WindowsPlatform_ReturnsFalse()
    {
        var cut = RenderWindows();
        var isLinux = (bool)typeof(WizardInstallStep)
            .GetProperty("IsLinux", Priv)!.GetValue(cut.Instance)!;
        Assert.False(isLinux);
    }

    // === DownloadUrl ===

    [Fact]
    public void DownloadUrl_Linux_ContainsTarGz()
    {
        var cut = RenderLinux();
        var url = (string)typeof(WizardInstallStep)
            .GetProperty("DownloadUrl", Priv)!.GetValue(cut.Instance)!;
        Assert.Contains("linux", url);
        Assert.Contains(".tar.gz", url);
    }

    [Fact]
    public void DownloadUrl_Windows_ContainsZip()
    {
        var cut = RenderWindows();
        var url = (string)typeof(WizardInstallStep)
            .GetProperty("DownloadUrl", Priv)!.GetValue(cut.Instance)!;
        Assert.Contains("win", url);
        Assert.Contains(".zip", url);
    }

    // === InstallerLink ===

    [Fact]
    public void InstallerLink_Linux_ContainsPlatformAndVersion()
    {
        var cut = RenderLinux(baseUrl: "https://my.server", version: "2.0.0", token: "secret");
        var link = (string)typeof(WizardInstallStep)
            .GetProperty("InstallerLink", Priv)!.GetValue(cut.Instance)!;
        Assert.Contains("linux", link);
        Assert.Contains("2.0.0", link);
        Assert.Contains("secret", link);
    }

    [Fact]
    public void InstallerLink_Windows_ContainsPlatform()
    {
        var cut = RenderWindows(baseUrl: "https://my.server", token: "tok");
        var link = (string)typeof(WizardInstallStep)
            .GetProperty("InstallerLink", Priv)!.GetValue(cut.Instance)!;
        Assert.Contains("windows", link);
    }

    [Fact]
    public void InstallerLink_EmptyBaseUrl_StartsWithSlash()
    {
        var cut = RenderLinux(baseUrl: "", token: "abc");
        var link = (string)typeof(WizardInstallStep)
            .GetProperty("InstallerLink", Priv)!.GetValue(cut.Instance)!;
        Assert.Contains("/api/agent/installer/linux", link);
    }

    [Fact]
    public void InstallerLink_EmptySiteVersion_DefaultsDev()
    {
        var cut = Render<WizardInstallStep>(p => p
            .Add(x => x.Platform, "linux")
            .Add(x => x.ServerBaseUrl, "https://s")
            .Add(x => x.SiteVersion, "")
            .Add(x => x.InstallerToken, "tok"));
        var link = (string)typeof(WizardInstallStep)
            .GetProperty("InstallerLink", Priv)!.GetValue(cut.Instance)!;
        Assert.Contains("dev", link);
    }

    // === LinkCopied ===

    [Fact]
    public void LinkCopied_InitiallyFalse()
    {
        var cut = RenderLinux(token: "tok");
        var copied = (bool)typeof(WizardInstallStep).GetField("_linkCopied", Priv)!.GetValue(cut.Instance)!;
        Assert.False(copied);
    }

    [Fact]
    public async Task CopyInstallerLinkAsync_EmptyToken_DoesNotSetCopied()
    {
        var cut = RenderLinux(token: null);
        var method = typeof(WizardInstallStep).GetMethod("CopyInstallerLinkAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var copied = (bool)typeof(WizardInstallStep).GetField("_linkCopied", Priv)!.GetValue(cut.Instance)!;
        Assert.False(copied);
    }

    [Fact]
    public async Task CopyInstallerLinkAsync_WithToken_SetsCopied()
    {
        var cut = RenderLinux(token: "valid-token");
        var method = typeof(WizardInstallStep).GetMethod("CopyInstallerLinkAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var copied = (bool)typeof(WizardInstallStep).GetField("_linkCopied", Priv)!.GetValue(cut.Instance)!;
        Assert.True(copied);
    }

    [Fact]
    public async Task OpenDownloadAsync_CallsJsOpen()
    {
        var cut = RenderLinux();
        var method = typeof(WizardInstallStep).GetMethod("OpenDownloadAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);
        // OpenDownloadAsync opens the platform download URL in a new tab via JS window.open.
        var open = Assert.Single(JSInterop.Invocations, i => i.Identifier == "open");
        Assert.Contains(".tar.gz", open.Arguments[0]!.ToString());
        Assert.Equal("_blank", open.Arguments[1]);
    }
}
