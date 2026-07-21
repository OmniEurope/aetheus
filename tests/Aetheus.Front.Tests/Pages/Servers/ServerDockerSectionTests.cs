// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerDockerSectionTests : BunitContext
{
    public ServerDockerSectionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
    }

    private readonly BunitTestHelper.TestHandler _handler;

    private static ServerDetailDto MakeDockerServer(int id = 20, string name = "method-test-srv") => new()
    {
        Id = id,
        Name = name,
        Hostname = "10.0.0.1",
        Type = ServerType.Docker,
        Status = ServerStatus.Online,
        CpuPercent = 45.2,
        MemoryUsedMb = 8192,
        MemoryTotalMb = 16384,
        DiskUsedGb = 50,
        DiskTotalGb = 200,
        Tags = ["prod"],
        Services = [],
        Docker = new DockerDataDto
        {
            Containers =
            [
                new DockerContainerDto { ContainerId = "abc123def456", Name = "nginx", Image = "nginx:latest", State = "running", CpuPercent = 5, MemoryUsageMb = 64, MemoryLimitMb = 256, Status = "Up 2h" },
                new DockerContainerDto { ContainerId = "def789ghi012", Name = "api", Image = "dotnet:8", State = "exited", CpuPercent = 0, MemoryUsageMb = 0, MemoryLimitMb = 512, Status = "Exited" }
            ],
            Images =
            [
                new DockerImageDto { ImageId = "i1", Repository = "nginx", Tag = "latest", Size = "142MB" },
                new DockerImageDto { ImageId = "i2", Repository = "dotnet", Tag = "8", Size = "800MB" }
            ],
            ComposeStacks =
            [
                new DockerComposeStackDto { Name = "monitoring", Status = "running(3)", RunningCount = 3, TotalCount = 3 }
            ],
            Networks = [new DockerNetworkDto { NetworkId = "n1", Name = "bridge", Driver = "bridge", Scope = "local" }],
            Volumes = [new DockerVolumeDto { Name = "pgdata", Driver = "local", Mountpoint = "/data" }]
        }
    };

    private IRenderedComponent<ServerDockerSection> RenderDockerSection()
    {
        var server = MakeDockerServer();
        _handler.SetJsonResponse("api/servers/20/docker/containers", new List<DockerContainerDto>());
        _handler.SetJsonResponse("api/servers/20/docker/action", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/containers/logs", "log output here");
        _handler.SetJsonResponse("api/servers/20/docker/images", new List<DockerImageDto>());
        _handler.SetJsonResponse("api/servers/20/docker/images/pull", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/compose/action", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/prune", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/resource-limits", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/containers/abc123def456/inspect", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/compose/monitoring/file", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/compose/file", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/exec", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/containers/abc123def456/env", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/containers/browse", "{}");
        _handler.SetJsonResponse("api/servers/20/docker/build", "{}");

        return Render<ServerDockerSection>(p => p
            .Add(x => x.Server, server)
            .Add(x => x.ServerId, 20)
            .Add(x => x.DockerInitialLoaded, true));
    }

    // === Static method tests ===

    [Theory]
    [InlineData("running", BadgeStyle.Success)]
    [InlineData("exited", BadgeStyle.Danger)]
    [InlineData("paused", BadgeStyle.Warning)]
    [InlineData("unknown", BadgeStyle.Light)]
    public void GetContainerBadge_ReturnsExpectedStyle(string state, BadgeStyle expected)
    {
        var method = typeof(ServerDockerSection).GetMethod("GetContainerBadge", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = Assert.IsType<BadgeStyle>(method.Invoke(null, [state]));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, 256, "docker-mem-bar docker-mem-green")]
    [InlineData(200, 256, "docker-mem-bar docker-mem-yellow")]
    [InlineData(240, 256, "docker-mem-bar docker-mem-red")]
    [InlineData(50, 0, "docker-mem-bar")]
    public void GetMemoryBarClass_ReturnsExpectedClass(double usage, double limit, string expected)
    {
        var method = typeof(ServerDockerSection).GetMethod("GetMemoryBarClass", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var container = new DockerContainerDto { MemoryUsageMb = usage, MemoryLimitMb = limit };
        var result = (string)method.Invoke(null, [container])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseEnvVars_ParsesValidJson()
    {
        var method = typeof(ServerDockerSection).GetMethod("ParseEnvVars", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var input = "[\"PATH=/usr/bin\",\"HOME=/root\",\"DB_HOST=localhost\"]";
        var result = (List<DockerEnvVarDto>)method.Invoke(null, [input])!;
        Assert.Equal(3, result.Count);
        Assert.Equal("PATH", result[0].Key);
        Assert.Equal("/usr/bin", result[0].Value);
        Assert.Equal("HOME", result[1].Key);
        Assert.Equal("DB_HOST", result[2].Key);
    }

    [Fact]
    public void ParseEnvVars_HandlesEmptyInput()
    {
        var method = typeof(ServerDockerSection).GetMethod("ParseEnvVars", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = (List<DockerEnvVarDto>)method.Invoke(null, ["[]"])!;
        Assert.Empty(result);
    }

    // === Instance method tests ===

    [Fact]
    public void PruneDialog_DefaultModel_SelectsAllResourceKinds()
    {
        var model = new DockerPruneDialogModel();

        Assert.True(model.Containers);
        Assert.True(model.Images);
        Assert.True(model.Volumes);
    }

    [Fact]
    public async Task DockerActionAsync_SendsAction()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("DockerActionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, ["abc123def456", DockerContainerAction.Stop])!;

        var target = typeof(ServerDockerSection).GetField("_dockerActionTarget", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Null(target);
    }

    [Fact]
    public async Task ConfirmRemoveContainerAsync_OpensDialogWithoutDeletingWhenCancelled()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("ConfirmRemoveContainerAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<DialogService>();
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["abc123def456", "nginx"])!);

        Assert.Equal(1, dialog.OpenCount);
        Assert.Equal("RemoveContainerConfirm", dialog.LastConfirmMessage);
        Assert.Equal("RemoveContainer", dialog.LastTitle);
        Assert.DoesNotContain(_handler.Requests, request => request.Method == "POST" && request.Url.Contains("docker/action"));
    }

    [Fact]
    public async Task ConfirmRemoveContainerAsync_ConfirmedSendsRemoveAction()
    {
        var cut = RenderDockerSection();

        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<DialogService>();
        dialog.ConfirmResult = true;
        var method = typeof(ServerDockerSection).GetMethod("ConfirmRemoveContainerAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["abc123def456", "nginx"])!);

        Assert.Contains(_handler.Requests, request => request.Method == "POST" && request.Url.Contains("docker/action"));
    }

    [Fact]
    public async Task ToggleLogsAsync_ToglesLogState()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("ToggleLogsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // First toggle - open
        await (Task)method.Invoke(cut.Instance, ["abc123def456"])!;
        var logsId = typeof(ServerDockerSection).GetField("_logsContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Equal("abc123def456", logsId);

        // Second toggle - close
        await (Task)method.Invoke(cut.Instance, ["abc123def456"])!;
        logsId = typeof(ServerDockerSection).GetField("_logsContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Null(logsId);
    }

    [Fact]
    public async Task PullImageAsync_WithEmptyName_DoesNothing()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("PullImageAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("docker/images/pull"));
    }

    [Fact]
    public async Task PullImageAsync_WithName_SendsRequest()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_pullImageName", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "nginx:latest");

        var method = typeof(ServerDockerSection).GetMethod("PullImageAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.Contains(_handler.Requests, r => r.Url.Contains("docker/images/pull"));
    }

    [Fact]
    public async Task RemoveImageAsync_SendsRequest()
    {
        _handler.SetJsonResponse("api/servers/20/docker/images/abc123def456", "{}");
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("RemoveImageAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, ["abc123def456"])!;

        // RemoveDockerImageAsync issues a DELETE to the image endpoint (image id in the path).
        Assert.Contains(_handler.Requests, r => r.Method == "DELETE" && r.Url.Contains("api/servers/20/docker/images/abc123def456"));
    }

    [Fact]
    public async Task ComposeActionAsync_SendsRequest()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("ComposeActionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, ["monitoring", DockerComposeAction.Up])!;

        // ExecuteComposeActionAsync POSTs the compose action to the compose/action endpoint.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/servers/20/docker/compose/action"));
    }

    [Fact]
    public void PruneDialog_RendersAllResourceChoices()
    {
        var cut = Render<DockerPruneDialog>();

        Assert.True(cut.FindAll("input").Count >= 3);
        Assert.Contains("DockerPrune", cut.Markup);
    }

    [Fact]
    public async Task ExecutePruneAsync_SendsRequest()
    {
        var cut = RenderDockerSection();

        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<DialogService>();
        dialog.OpenResult = new DockerPruneDialogResult(true, true, false);
        var method = typeof(ServerDockerSection).GetMethod("OpenPruneDialogAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        Assert.Contains(_handler.Requests, r => r.Url.Contains("docker/prune"));
    }

    [Fact]
    public async Task OpenResourceLimitsDialog_PrefillsContainerLimits()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("OpenResourceLimitsDialog", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<DialogService>();
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["abc123def456"])!);

        Assert.Equal("abc123def456", dialog.LastParameters!["ContainerId"]);
        Assert.Equal(256, dialog.LastParameters["MemoryLimitMb"]);
    }

    [Fact]
    public async Task ApplyResourceLimitsAsync_SendsRequest()
    {
        var cut = RenderDockerSection();

        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<DialogService>();
        dialog.OpenResult = new DockerResourceLimitsDialogResult(1.5, 512);
        var method = typeof(ServerDockerSection).GetMethod("OpenResourceLimitsDialog", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["abc123def456"])!);

        Assert.Contains(_handler.Requests, r => r.Url.Contains("docker/resource-limits"));
    }

    [Fact]
    public void OnContainerRowRender_SetsClassForRunning()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("OnContainerRowRender", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var argsType = typeof(RowRenderEventArgs<DockerContainerDto>);
        var args = Activator.CreateInstance(argsType)!;
        argsType.GetProperty("Data")!.SetValue(args, new DockerContainerDto { State = "running" });
        var attrs = new Dictionary<string, object>();
        argsType.GetProperty("Attributes")!.SetValue(args, attrs);
        method.Invoke(cut.Instance, [args]);

        Assert.Equal("docker-row-running", attrs["class"]);
    }

    [Fact]
    public void OnContainerRowRender_SetsClassForExited()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("OnContainerRowRender", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var argsType = typeof(RowRenderEventArgs<DockerContainerDto>);
        var args = Activator.CreateInstance(argsType)!;
        argsType.GetProperty("Data")!.SetValue(args, new DockerContainerDto { State = "exited" });
        var attrs = new Dictionary<string, object>();
        argsType.GetProperty("Attributes")!.SetValue(args, attrs);
        method.Invoke(cut.Instance, [args]);

        Assert.Equal("docker-row-exited", attrs["class"]);
    }

    [Fact]
    public void OnContainerRowRender_NullAttributes_DoesNothing()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("OnContainerRowRender", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var argsType = typeof(RowRenderEventArgs<DockerContainerDto>);
        var args = Activator.CreateInstance(argsType)!;
        method.Invoke(cut.Instance, [args]);
    }

    [Fact]
    public async Task RequestInspectAsync_TogglesInspect()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("RequestInspectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // First call - open
        await (Task)method.Invoke(cut.Instance, ["abc123def456"])!;
        var inspectId = typeof(ServerDockerSection).GetField("_inspectContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Equal("abc123def456", inspectId);

        // Second call - close
        await (Task)method.Invoke(cut.Instance, ["abc123def456"])!;
        inspectId = typeof(ServerDockerSection).GetField("_inspectContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Null(inspectId);
    }

    [Fact]
    public async Task OpenComposeEditorAsync_SetsFields()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("OpenComposeEditorAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, ["monitoring"])!;

        var visible = (bool)typeof(ServerDockerSection).GetField("_composeEditorVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.True(visible);

        var stack = (string)typeof(ServerDockerSection).GetField("_composeEditorStack", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal("monitoring", stack);
    }

    [Fact]
    public void CloseComposeEditor_ResetsFields()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_composeEditorVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, true);
        typeof(ServerDockerSection).GetField("_composeEditorStack", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "monitoring");

        var method = typeof(ServerDockerSection).GetMethod("CloseComposeEditor", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, []);

        var visible = (bool)typeof(ServerDockerSection).GetField("_composeEditorVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.False(visible);
    }

    [Fact]
    public async Task SaveComposeFileAsync_WithEmptyContent_DoesNothing()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_composeEditorContent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, string.Empty);

        var method = typeof(ServerDockerSection).GetMethod("SaveComposeFileAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        // Empty content short-circuits before any HTTP: no compose-file PUT/POST is emitted.
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("docker/compose") && r.Url.Contains("/file"));
    }

    [Fact]
    public async Task SaveComposeFileAsync_WithContent_SendsRequest()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_composeEditorStack", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "monitoring");
        typeof(ServerDockerSection).GetField("_composeEditorContent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "version: '3'");
        typeof(ServerDockerSection).GetField("_composeEditorVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, true);

        var method = typeof(ServerDockerSection).GetMethod("SaveComposeFileAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.Contains(_handler.Requests, r => r.Url.Contains("docker/compose") && r.Url.Contains("/file"));
    }

    [Fact]
    public void OpenShell_TogglesShellState()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("OpenShell", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Open
        method.Invoke(cut.Instance, ["abc123def456", "nginx"]);
        var shellId = typeof(ServerDockerSection).GetField("_shellContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Equal("abc123def456", shellId);

        // Close (same container ID)
        method.Invoke(cut.Instance, ["abc123def456", "nginx"]);
        shellId = typeof(ServerDockerSection).GetField("_shellContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Null(shellId);
    }

    [Fact]
    public async Task SendShellCommandAsync_WithEmptyCommand_DoesNothing()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_shellContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "abc123def456");
        typeof(ServerDockerSection).GetField("_shellCommand", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, string.Empty);

        var method = typeof(ServerDockerSection).GetMethod("SendShellCommandAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        var output = (string)typeof(ServerDockerSection).GetField("_shellOutput", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Empty(output);
        // An empty command must not reach the agent.
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("docker/exec"));
    }

    [Fact]
    public async Task SendShellCommandAsync_WithCommand_SendsRequest()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_shellContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "abc123def456");
        typeof(ServerDockerSection).GetField("_shellCommand", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "ls -la");

        var method = typeof(ServerDockerSection).GetMethod("SendShellCommandAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        var output = (string)typeof(ServerDockerSection).GetField("_shellOutput", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Contains("$ ls -la", output);
        // The command is dispatched to the agent via a POST to the docker/exec endpoint.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/servers/20/docker/exec"));
    }

    [Fact]
    public async Task RequestEnvVarsAsync_TogglesEnvVars()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("RequestEnvVarsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Open
        await (Task)method.Invoke(cut.Instance, ["abc123def456", "nginx"])!;
        var envId = typeof(ServerDockerSection).GetField("_envContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Equal("abc123def456", envId);

        // Close
        await (Task)method.Invoke(cut.Instance, ["abc123def456", "nginx"])!;
        envId = typeof(ServerDockerSection).GetField("_envContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Null(envId);
    }

    [Fact]
    public async Task OpenFileBrowserAsync_TogglesFileBrowser()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("OpenFileBrowserAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Open
        await (Task)method.Invoke(cut.Instance, ["abc123def456", "nginx"])!;
        var browseId = typeof(ServerDockerSection).GetField("_browseContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Equal("abc123def456", browseId);

        // Close
        await (Task)method.Invoke(cut.Instance, ["abc123def456", "nginx"])!;
        browseId = typeof(ServerDockerSection).GetField("_browseContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Null(browseId);
    }

    [Fact]
    public async Task BrowseParentAsync_AtRoot_DoesNothing()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_browsePath", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "/");

        var method = typeof(ServerDockerSection).GetMethod("BrowseParentAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        var path = (string)typeof(ServerDockerSection).GetField("_browsePath", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal("/", path);
    }

    [Fact]
    public async Task BrowseParentAsync_InSubDirectory_GoesUp()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_browseContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "abc123def456");
        typeof(ServerDockerSection).GetField("_browsePath", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "/var/log");

        var method = typeof(ServerDockerSection).GetMethod("BrowseParentAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        var path = (string)typeof(ServerDockerSection).GetField("_browsePath", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal("/var", path);
    }

    [Fact]
    public async Task BuildImageAsync_WithEmptyTag_DoesNothing()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_buildImageTag", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, string.Empty);

        var method = typeof(ServerDockerSection).GetMethod("BuildImageAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        // Empty tag short-circuits before the build request - nothing is posted to docker/build.
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("docker/build"));
    }

    [Fact]
    public async Task BuildImageAsync_WithContent_SendsRequest()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_buildImageTag", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "myapp:latest");
        typeof(ServerDockerSection).GetField("_buildDockerfileContent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "FROM alpine\nRUN echo hello");

        var method = typeof(ServerDockerSection).GetMethod("BuildImageAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        Assert.Contains(_handler.Requests, r => r.Url.Contains("docker/build"));
    }

    [Fact]
    public async Task RefreshDockerAsync_UpdatesContainers()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("RefreshDockerAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        // Refresh re-fetches the container list from the server (GET docker/containers).
        Assert.Contains(_handler.Requests, r => r.Method == "GET" && r.Url.Contains("api/servers/20/docker/containers"));
    }

    [Fact]
    public async Task CopyInspectToClipboardAsync_WithNullContent_DoesNothing()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_inspectContent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, null);

        var method = typeof(ServerDockerSection).GetMethod("CopyInspectToClipboardAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        // Null inspect content short-circuits before the JS copy - no clipboard interop fires.
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "dockerInterop.copyToClipboard");
    }

    [Fact]
    public void OnAutoRefreshChanged_True_CreatesLoop()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("OnAutoRefreshChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, [true]);

        var task = typeof(ServerDockerSection).GetField("_autoRefreshTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.NotNull(task);

        // Disable to dispose timer
        method.Invoke(cut.Instance, [false]);
        var cts = (CancellationTokenSource?)typeof(ServerDockerSection)
            .GetField("_autoRefreshCts", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        Assert.True(cts is null || cts.IsCancellationRequested);
    }

    [Fact]
    public async Task ServerChange_ResetsTransientStateAndIgnoresOldNotifications()
    {
        var cut = RenderDockerSection();
        typeof(ServerDockerSection).GetField("_shellContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cut.Instance, "abc123def456");
        typeof(ServerDockerSection).GetField("_inspectContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cut.Instance, "abc123def456");

        var nextServer = MakeDockerServer(21, "next-server");
        cut.Render(parameters => parameters
            .Add(component => component.Server, nextServer)
            .Add(component => component.ServerId, 21)
            .Add(component => component.DockerInitialLoaded, true));

        Assert.Null(typeof(ServerDockerSection).GetField("_shellContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance));
        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            TaskId = 1,
            ServerId = 20,
            TaskName = "docker inspect",
            Status = TaskExecutionStatus.Success,
            Output = "old-server-output"
        }));
        Assert.Null(typeof(ServerDockerSection).GetField("_inspectContent", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance));
    }

    [Fact]
    public async Task PruneAsync_WithFlags_SendsRequest()
    {
        var cut = RenderDockerSection();

        var method = typeof(ServerDockerSection).GetMethod("PruneAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [true, false, true])!;

        Assert.Contains(_handler.Requests, r => r.Url.Contains("docker/prune"));
    }

    [Fact]
    public async Task OnShellKeyDown_Enter_SendsCommand()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_shellContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "abc123def456");
        typeof(ServerDockerSection).GetField("_shellCommand", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "pwd");

        var method = typeof(ServerDockerSection).GetMethod("OnShellKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var args = new KeyboardEventArgs { Key = "Enter" };
        await (Task)method.Invoke(cut.Instance, [args])!;

        var output = (string)typeof(ServerDockerSection).GetField("_shellOutput", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Contains("$ pwd", output);
        // Enter dispatches the buffered command to the agent (POST docker/exec).
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/servers/20/docker/exec"));
    }

    [Fact]
    public async Task OnShellKeyDown_NonEnter_DoesNotSend()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_shellContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "abc123def456");
        typeof(ServerDockerSection).GetField("_shellCommand", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "pwd");

        var method = typeof(ServerDockerSection).GetMethod("OnShellKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var args = new KeyboardEventArgs { Key = "a" };
        await (Task)method.Invoke(cut.Instance, [args])!;

        var output = (string)typeof(ServerDockerSection).GetField("_shellOutput", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Empty(output);
        // A non-Enter keypress must not dispatch the command.
        Assert.DoesNotContain(_handler.Requests, r => r.Url.Contains("docker/exec"));
    }

    [Fact]
    public void FilteredContainers_WithSearch_FiltersCorrectly()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_containerSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "nginx");

        var prop = typeof(ServerDockerSection).GetProperty("FilteredContainers", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var filtered = (List<DockerContainerDto>)prop.GetValue(cut.Instance)!;

        Assert.Single(filtered);
        Assert.Equal("nginx", filtered[0].Name);
    }

    [Fact]
    public void FilteredImages_WithSearch_FiltersCorrectly()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_imageSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "dotnet");

        var prop = typeof(ServerDockerSection).GetProperty("FilteredImages", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var filtered = (List<DockerImageDto>)prop.GetValue(cut.Instance)!;

        Assert.Single(filtered);
        Assert.Equal("dotnet", filtered[0].Repository);
    }

    [Fact]
    public void FilteredCompose_WithSearch_FiltersCorrectly()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_composeSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "monitoring");

        var prop = typeof(ServerDockerSection).GetProperty("FilteredCompose", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var filtered = (List<DockerComposeStackDto>)prop.GetValue(cut.Instance)!;

        Assert.Single(filtered);
    }

    [Fact]
    public void FilteredNetworks_WithSearch_FiltersCorrectly()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_networkSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "bridge");

        var prop = typeof(ServerDockerSection).GetProperty("FilteredNetworks", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var filtered = (List<DockerNetworkDto>)prop.GetValue(cut.Instance)!;

        Assert.Single(filtered);
    }

    [Fact]
    public void FilteredVolumes_WithSearch_FiltersCorrectly()
    {
        var cut = RenderDockerSection();

        typeof(ServerDockerSection).GetField("_volumeSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "pgdata");

        var prop = typeof(ServerDockerSection).GetProperty("FilteredVolumes", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var filtered = (List<DockerVolumeDto>)prop.GetValue(cut.Instance)!;

        Assert.Single(filtered);
    }

    // === HandleTaskCompleted / DispatchTaskOutput ===

    [Fact]
    public async Task HandleTaskCompleted_InspectOutput_SetsInspectContent()
    {
        var cut = RenderDockerSection();
        typeof(ServerDockerSection).GetField("_inspectContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "abc123def456");

        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            TaskId = 1,
            ServerId = 20,
            TaskName = "docker inspect abc123def456",
            Status = TaskExecutionStatus.Success,
            Output = "{\"Id\": \"abc123\"}"
        }));

        var content = (string?)typeof(ServerDockerSection).GetField("_inspectContent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Equal("{\"Id\": \"abc123\"}", content);
    }

    [Fact]
    public async Task HandleTaskCompleted_ComposeFileOutput_SetsComposeContent()
    {
        var cut = RenderDockerSection();
        typeof(ServerDockerSection).GetField("_composeEditorVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, true);
        typeof(ServerDockerSection).GetField("_composeFileLoading", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, true);

        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            TaskId = 2,
            ServerId = 20,
            TaskName = "docker compose file read",
            Status = TaskExecutionStatus.Success,
            Output = "version: '3'\nservices:\n  web:\n    image: nginx"
        }));

        var content = (string?)typeof(ServerDockerSection).GetField("_composeEditorContent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        var loading = (bool)typeof(ServerDockerSection).GetField("_composeFileLoading", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Contains("nginx", content!);
        Assert.False(loading);
    }

    [Fact]
    public async Task HandleTaskCompleted_ExecOutput_AppendsToShellOutput()
    {
        var cut = RenderDockerSection();
        typeof(ServerDockerSection).GetField("_shellContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "abc123def456");
        typeof(ServerDockerSection).GetField("_shellOutput", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "");

        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            TaskId = 3,
            ServerId = 20,
            TaskName = "docker exec abc123",
            Status = TaskExecutionStatus.Success,
            Output = "/app"
        }));

        var output = (string)typeof(ServerDockerSection).GetField("_shellOutput", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Contains("/app", output);
    }

    [Fact]
    public async Task HandleTaskCompleted_EnvOutput_SetsEnvContent()
    {
        var cut = RenderDockerSection();
        typeof(ServerDockerSection).GetField("_envContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "abc123def456");

        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            TaskId = 4,
            ServerId = 20,
            TaskName = "docker env abc123",
            Status = TaskExecutionStatus.Success,
            Output = "[\"PATH=/usr/bin\",\"HOME=/root\"]"
        }));

        var content = (string?)typeof(ServerDockerSection).GetField("_envContent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Contains("PATH", content!);
    }

    [Fact]
    public async Task HandleTaskCompleted_BrowseOutput_SetsBrowseContent()
    {
        var cut = RenderDockerSection();
        typeof(ServerDockerSection).GetField("_browseContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "abc123def456");

        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            TaskId = 5,
            ServerId = 20,
            TaskName = "docker ls /app",
            Status = TaskExecutionStatus.Success,
            Output = "bin  lib  src"
        }));

        var content = (string?)typeof(ServerDockerSection).GetField("_browseContent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Equal("bin  lib  src", content);
    }

    [Fact]
    public async Task HandleTaskCompleted_NullOutput_DoesNotDispatch()
    {
        var cut = RenderDockerSection();
        typeof(ServerDockerSection).GetField("_inspectContainerId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "abc123def456");

        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            TaskId = 6,
            ServerId = 20,
            TaskName = "docker inspect abc123",
            Status = TaskExecutionStatus.Success,
            Output = null
        }));

        var content = (string?)typeof(ServerDockerSection).GetField("_inspectContent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Null(content);
    }

    [Fact]
    public async Task HandleTaskCompleted_UnmatchedTask_DoesNotSetAnyField()
    {
        var cut = RenderDockerSection();

        await cut.InvokeAsync(() => cut.Instance.HandleTaskCompleted(new TaskCompletedNotification
        {
            TaskId = 7,
            ServerId = 20,
            TaskName = "docker stats",
            Status = TaskExecutionStatus.Success,
            Output = "some output"
        }));

        var inspect = (string?)typeof(ServerDockerSection).GetField("_inspectContent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        var env = (string?)typeof(ServerDockerSection).GetField("_envContent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        var browse = (string?)typeof(ServerDockerSection).GetField("_browseContent", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Null(inspect);
        Assert.Null(env);
        Assert.Null(browse);
    }

    // === GetProjectAccentClass ===

    [Theory]
    [InlineData("myproject")]
    [InlineData("another-project")]
    [InlineData("backend-api")]
    public void GetProjectAccentClass_NonEmptyProject_ReturnsAccentClass(string project)
    {
        var result = ServerDockerSection.GetProjectAccentClass(project);
        Assert.StartsWith("docker-project-accent-", result);
        var index = int.Parse(result["docker-project-accent-".Length..]);
        Assert.InRange(index, 0, 7);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void GetProjectAccentClass_EmptyProject_ReturnsEmpty(string? project)
    {
        Assert.Empty(ServerDockerSection.GetProjectAccentClass(project));
    }

    [Fact]
    public void GetProjectAccentClass_SameProject_ReturnsSameClass()
    {
        Assert.Equal(
            ServerDockerSection.GetProjectAccentClass("myapp"),
            ServerDockerSection.GetProjectAccentClass("myapp"));
    }

    [Fact]
    public void GetProjectAccentClass_DifferentProjects_ProduceMultipleColors()
    {
        var classes = new HashSet<string>();
        foreach (var p in new[] { "app1", "app2", "app3", "app4", "app5", "app6", "app7", "app8" })
            classes.Add(ServerDockerSection.GetProjectAccentClass(p));
        Assert.True(classes.Count > 1);
    }
}
