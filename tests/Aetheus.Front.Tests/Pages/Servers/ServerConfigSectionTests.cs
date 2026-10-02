// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerConfigSectionTests : BunitContext
{
    public ServerConfigSectionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private readonly BunitTestHelper.TestHandler _handler;

    private IRenderedComponent<ServerConfigSection> RenderConfigSection()
    {
        _handler.SetJsonResponse("api/servers/20/configuration/export", "server:\n  name: test");
        _handler.SetJsonResponse("api/servers/20/configuration/validate", new { IsValid = true, Errors = Array.Empty<string>() });
        _handler.SetJsonResponse("api/servers/20/configuration/preview", new { Changes = Array.Empty<object>() });
        _handler.SetJsonResponse("api/servers/20/configuration/deploy", new { TasksCreated = 3 });

        return Render<ServerConfigSection>(p => p.Add(x => x.ServerId, 20));
    }

    [Fact]
    public async Task ExportConfigAsync_SetsYaml()
    {
        var cut = RenderConfigSection();

        var method = typeof(ServerConfigSection).GetMethod("ExportConfigAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        // Export GETs the server configuration and stores the returned YAML for display/copy.
        Assert.Contains(_handler.Requests, r => r.Method == "GET" && r.Url.Contains("configuration/export"));
        var yaml = (string)typeof(ServerConfigSection).GetField("_configYaml", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Contains("name: test", yaml);
    }

    [Fact]
    public async Task ValidateConfigAsync_WithEmptyYaml_MakesNoRequest()
    {
        var cut = RenderConfigSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, string.Empty);

        var method = typeof(ServerConfigSection).GetMethod("ValidateConfigAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("configuration/validate"));
    }

    [Fact]
    public async Task ValidateConfigAsync_WithYaml_SendsRequest()
    {
        var cut = RenderConfigSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "server:\n  name: test");

        var method = typeof(ServerConfigSection).GetMethod("ValidateConfigAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.Contains(_handler.Requests, r => r.Url.Contains("configuration/validate"));
    }

    [Fact]
    public async Task PreviewConfigAsync_WithEmptyYaml_MakesNoRequest()
    {
        var cut = RenderConfigSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, string.Empty);

        var method = typeof(ServerConfigSection).GetMethod("PreviewConfigAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("configuration/preview"));
    }

    [Fact]
    public async Task PreviewConfigAsync_WithYaml_SendsRequest()
    {
        var cut = RenderConfigSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "server:\n  name: test");

        var method = typeof(ServerConfigSection).GetMethod("PreviewConfigAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.Contains(_handler.Requests, r => r.Url.Contains("configuration/preview"));
    }

    [Fact]
    public async Task DeployConfigAsync_WithEmptyYaml_MakesNoRequest()
    {
        var cut = RenderConfigSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, string.Empty);

        var method = typeof(ServerConfigSection).GetMethod("DeployConfigAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("configuration/deploy"));
    }

    [Fact]
    public async Task DeployConfigAsync_WithYaml_SendsRequest()
    {
        var cut = RenderConfigSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "server:\n  name: test");
        typeof(ServerConfigSection).GetField("_configImportVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, true);

        var method = typeof(ServerConfigSection).GetMethod("DeployConfigAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.Contains(_handler.Requests, r => r.Url.Contains("configuration/deploy"));
    }

    [Theory]
    [InlineData("pull", OmniTone.Success)]
    [InlineData("create", OmniTone.Success)]
    [InlineData("deploy", OmniTone.Success)]
    [InlineData("enable", OmniTone.Success)]
    [InlineData("update", OmniTone.Accent)]
    [InlineData("unchanged", OmniTone.Neutral)]
    [InlineData("unknown", OmniTone.Neutral)]
    public void GetChangeBadgeStyle_ReturnsCorrectStyle(string action, OmniTone expected)
    {
        var method = typeof(ServerConfigSection).GetMethod("GetChangeBadgeStyle", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (OmniTone)method.Invoke(null, [action])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void R516_TheConfiguration_IsATitledCodeBlockWithItsOwnCopyButton()
    {
        var cut = RenderConfigSection();
        typeof(ServerConfigSection).GetField("_configYaml", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "test yaml");
        cut.Render();

        var block = cut.Find(".omni-code-block.config-yaml-block");
        Assert.Contains("CurrentConfiguration", block.TextContent, StringComparison.Ordinal);
        Assert.Contains("test yaml", block.QuerySelector("pre")!.TextContent, StringComparison.Ordinal);
        Assert.NotNull(block.QuerySelector("figcaption button"));
    }
}
