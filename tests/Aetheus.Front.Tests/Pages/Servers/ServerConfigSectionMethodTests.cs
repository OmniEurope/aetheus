// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerConfigSectionMethodTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerConfigSectionMethodTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    // === GetChangeBadgeStyle (private static) ===

    [Theory]
    [InlineData("pull", OmniTone.Success)]
    [InlineData("create", OmniTone.Success)]
    [InlineData("deploy", OmniTone.Success)]
    [InlineData("enable", OmniTone.Success)]
    [InlineData("update", OmniTone.Accent)]
    [InlineData("unchanged", OmniTone.Neutral)]
    [InlineData("unknown", OmniTone.Neutral)]
    [InlineData("delete", OmniTone.Neutral)]
    public void GetChangeBadgeStyle_ReturnsExpected(string action, OmniTone expected)
    {
        var method = typeof(ServerConfigSection).GetMethod("GetChangeBadgeStyle", PrivStatic)!;
        var result = (OmniTone)method.Invoke(null, [action])!;
        Assert.Equal(expected, result);
    }

    // === Instance methods via render ===

    private IRenderedComponent<ServerConfigSection> RenderSection()
    {
        _handler.SetJsonResponse("api/servers/1/configuration/export", "\"yaml: content\"");
        _handler.SetJsonResponse("api/servers/1/configuration/validate", new ServerConfigValidationResult());
        _handler.SetJsonResponse("api/servers/1/configuration/preview", new ServerConfigPreviewDto { Changes = [] });
        _handler.SetJsonResponse("api/servers/1/configuration/deploy", new ServerConfigDeployResultDto { TasksCreated = 3 });
        return Render<ServerConfigSection>(p => p.Add(x => x.ServerId, 1));
    }

    [Fact]
    public async Task ExportConfigAsync_SetsConfigYaml()
    {
        var cut = RenderSection();
        var method = typeof(ServerConfigSection).GetMethod("ExportConfigAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        var yaml = (string)typeof(ServerConfigSection).GetField("_configYaml", Priv)!.GetValue(cut.Instance)!;
        Assert.NotEmpty(yaml);
    }

    [Fact]
    public async Task ValidateConfigAsync_EmptyYaml_MakesNoRequest()
    {
        var cut = RenderSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", Priv)!.SetValue(cut.Instance, "");

        var method = typeof(ServerConfigSection).GetMethod("ValidateConfigAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        // A360-09: _configValidating is false in BOTH branches (set true then false again), so
        // asserting it proved nothing: deleting the guard left this test green. The guard's actual
        // effect is that no call is made, so that is what is asserted now.
        Assert.DoesNotContain(_handler.Requests, request => request.Url.Contains("configuration/validate"));
    }

    [Fact]
    public async Task ValidateConfigAsync_WithYaml_SetsResult()
    {
        var cut = RenderSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", Priv)!.SetValue(cut.Instance, "yaml: true");

        var method = typeof(ServerConfigSection).GetMethod("ValidateConfigAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        var result = typeof(ServerConfigSection).GetField("_configValidation", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task PreviewConfigAsync_EmptyYaml_MakesNoRequest()
    {
        var cut = RenderSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", Priv)!.SetValue(cut.Instance, " ");

        var method = typeof(ServerConfigSection).GetMethod("PreviewConfigAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.DoesNotContain(_handler.Requests, request => request.Url.Contains("configuration/preview"));
    }

    [Fact]
    public async Task PreviewConfigAsync_WithYaml_SetsPreview()
    {
        var cut = RenderSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", Priv)!.SetValue(cut.Instance, "yaml: true");

        var method = typeof(ServerConfigSection).GetMethod("PreviewConfigAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        var result = typeof(ServerConfigSection).GetField("_configPreview", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DeployConfigAsync_EmptyYaml_MakesNoRequest()
    {
        var cut = RenderSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", Priv)!.SetValue(cut.Instance, "");

        var method = typeof(ServerConfigSection).GetMethod("DeployConfigAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.DoesNotContain(_handler.Requests, request => request.Url.Contains("configuration/deploy"));
    }

    [Fact]
    public async Task DeployConfigAsync_WithYaml_ClearsState()
    {
        var cut = RenderSection();
        typeof(ServerConfigSection).GetField("_configImportYaml", Priv)!.SetValue(cut.Instance, "yaml: true");
        typeof(ServerConfigSection).GetField("_configImportVisible", Priv)!.SetValue(cut.Instance, true);

        var method = typeof(ServerConfigSection).GetMethod("DeployConfigAsync", Priv)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        var visible = (bool)typeof(ServerConfigSection).GetField("_configImportVisible", Priv)!.GetValue(cut.Instance)!;
        var deploying = (bool)typeof(ServerConfigSection).GetField("_configDeploying", Priv)!.GetValue(cut.Instance)!;
        Assert.False(visible);
        Assert.False(deploying);
    }
}
