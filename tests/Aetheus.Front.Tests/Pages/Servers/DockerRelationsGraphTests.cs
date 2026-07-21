// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class DockerRelationsGraphTests : BunitContext
{
    public DockerRelationsGraphTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    private static DockerDataDto MakeDockerData() => new()
    {
        Containers =
        [
            new DockerContainerDto { ContainerId = "c1", Name = "web", Image = "nginx:latest", State = "running", Project = "myapp" },
            new DockerContainerDto { ContainerId = "c2", Name = "api", Image = "dotnet:8", State = "exited", Project = "myapp" },
            new DockerContainerDto { ContainerId = "c3", Name = "db", Image = "postgres:16", State = "running", Project = "infra" }
        ],
        Images =
        [
            new DockerImageDto { ImageId = "i1", Repository = "nginx", Tag = "latest", Project = "myapp" },
            new DockerImageDto { ImageId = "i2", Repository = "dotnet", Tag = "8", Project = "myapp" },
            new DockerImageDto { ImageId = "i3", Repository = "postgres", Tag = "16", Project = "infra" },
            new DockerImageDto { ImageId = "i4", Repository = "orphan", Tag = "<none>" }
        ],
        Networks =
        [
            new DockerNetworkDto { NetworkId = "n1", Name = "bridge", Driver = "bridge", Scope = "local", Project = "myapp" },
            new DockerNetworkDto { NetworkId = "n2", Name = "infra-net", Driver = "overlay", Scope = "swarm", Project = "infra" }
        ],
        Volumes =
        [
            new DockerVolumeDto { Name = "pgdata", Driver = "local", Mountpoint = "/data", Project = "infra" },
            new DockerVolumeDto { Name = "cache", Driver = "local", Mountpoint = "/cache", Project = "myapp" }
        ],
        ComposeStacks = []
    };

    [Fact]
    public void Renders_WithDockerData()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));
        // The graph renders its SVG canvas plus the per-kind filter labels.
        Assert.Contains("docker-graph-svg", cut.Markup);
        Assert.Contains("NodeType", cut.Markup);
    }

    [Fact]
    public void OnParametersSet_BuildsProjectOptions()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        var options = (List<string>)typeof(DockerRelationsGraph)
            .GetField("_projectOptions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;

        Assert.Contains("myapp", options);
        Assert.Contains("infra", options);
    }

    [Fact]
    public void BuildData_IncludesAllNodeKinds()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        var method = typeof(DockerRelationsGraph).GetMethod("BuildData", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var json = (string)method.Invoke(cut.Instance, [])!;

        Assert.Contains("\"group\":\"container\"", json);
        Assert.Contains("\"group\":\"image\"", json);
        Assert.Contains("\"group\":\"network\"", json);
        Assert.Contains("\"group\":\"volume\"", json);
        Assert.Contains("\"group\":\"project\"", json);
    }

    [Fact]
    public void BuildData_WithProjectFilter_FiltersNodes()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        typeof(DockerRelationsGraph)
            .GetField("_selectedProject", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cut.Instance, "infra");

        var method = typeof(DockerRelationsGraph).GetMethod("BuildData", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var json = (string)method.Invoke(cut.Instance, [])!;

        Assert.Contains("infra", json);
        Assert.DoesNotContain("\"label\":\"web\"", json);
    }

    [Fact]
    public void BuildData_DisabledKindFilter_ExcludesNodes()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        var kindFilter = (Dictionary<string, bool>)typeof(DockerRelationsGraph)
            .GetField("_kindFilter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        kindFilter["network"] = false;
        kindFilter["volume"] = false;

        var method = typeof(DockerRelationsGraph).GetMethod("BuildData", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var json = (string)method.Invoke(cut.Instance, [])!;

        Assert.DoesNotContain("\"group\":\"network\"", json);
        Assert.DoesNotContain("\"group\":\"volume\"", json);
        Assert.Contains("\"group\":\"container\"", json);
    }

    [Fact]
    public void BuildData_ImageTagNone_UsesRepositoryOnly()
    {
        var docker = new DockerDataDto
        {
            Containers = [],
            Images = [new DockerImageDto { ImageId = "i1", Repository = "orphan", Tag = "<none>" }],
            Networks = [],
            Volumes = [],
            ComposeStacks = []
        };

        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, docker));

        var method = typeof(DockerRelationsGraph).GetMethod("BuildData", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var json = (string)method.Invoke(cut.Instance, [])!;

        Assert.Contains("\"label\":\"orphan\"", json);
        Assert.DoesNotContain("<none>", json);
    }

    [Fact]
    public async Task OnGraphNodeClicked_Container_SetsSelectedNode()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        await cut.InvokeAsync(() => cut.Instance.OnGraphNodeClicked("c:c1"));

        var label = (string?)typeof(DockerRelationsGraph)
            .GetField("_selectedNodeLabel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        var kind = (string?)typeof(DockerRelationsGraph)
            .GetField("_selectedNodeKind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);

        Assert.Equal("web", label);
        Assert.Equal("Containers", kind);
    }

    [Fact]
    public async Task OnGraphNodeClicked_Network_SetsSelectedNode()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        await cut.InvokeAsync(() => cut.Instance.OnGraphNodeClicked("n:n1"));

        var label = (string?)typeof(DockerRelationsGraph)
            .GetField("_selectedNodeLabel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);

        Assert.Equal("bridge", label);
    }

    [Fact]
    public async Task OnGraphNodeClicked_Project_SetsProjectKind()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        await cut.InvokeAsync(() => cut.Instance.OnGraphNodeClicked("p:myapp"));

        var kind = (string?)typeof(DockerRelationsGraph)
            .GetField("_selectedNodeKind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        Assert.Equal("Project", kind);
    }

    [Fact]
    public async Task OnGraphNodeClicked_Image_SetsImageKind()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        await cut.InvokeAsync(() => cut.Instance.OnGraphNodeClicked("i:nginx:latest"));

        var kind = (string?)typeof(DockerRelationsGraph)
            .GetField("_selectedNodeKind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        Assert.Equal("DockerImages", kind);
    }

    [Fact]
    public async Task OnGraphNodeClicked_Volume_SetsVolumeKind()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        await cut.InvokeAsync(() => cut.Instance.OnGraphNodeClicked("v:pgdata"));

        var kind = (string?)typeof(DockerRelationsGraph)
            .GetField("_selectedNodeKind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        Assert.Equal("DockerVolumes", kind);
    }

    [Fact]
    public async Task OnGraphNodeClicked_Empty_DoesNothing()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        await cut.InvokeAsync(() => cut.Instance.OnGraphNodeClicked(""));

        var kind = (string?)typeof(DockerRelationsGraph)
            .GetField("_selectedNodeKind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        Assert.Null(kind);
    }

    [Fact]
    public async Task OnGraphNodeClicked_UnknownPrefix_SetsQuestionMark()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        await cut.InvokeAsync(() => cut.Instance.OnGraphNodeClicked("x:unknown"));

        var kind = (string?)typeof(DockerRelationsGraph)
            .GetField("_selectedNodeKind", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance);
        Assert.Equal("?", kind);
    }

    [Fact]
    public async Task DisposeAsync_WithNullModule_DoesNotThrow()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));
        await ((IAsyncDisposable)cut.Instance).DisposeAsync();
        // The JS module was never imported (no OnAfterRender in bUnit), so disposing the
        // null module is a no-op and a second dispose stays exception-free.
        var ex = await Record.ExceptionAsync(async () => await ((IAsyncDisposable)cut.Instance).DisposeAsync());
        Assert.Null(ex);
    }

    [Fact]
    public void BuildData_EmptyDocker_ReturnsEmptyGraph()
    {
        var docker = new DockerDataDto
        {
            Containers = [],
            Images = [],
            Networks = [],
            Volumes = [],
            ComposeStacks = []
        };
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, docker));

        var method = typeof(DockerRelationsGraph).GetMethod("BuildData", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var json = (string)method.Invoke(cut.Instance, [])!;

        Assert.Contains("\"nodes\":[]", json);
        Assert.Contains("\"edges\":[]", json);
    }

    [Fact]
    public void NodeKinds_ReturnsFourEntries()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        var prop = typeof(DockerRelationsGraph).GetProperty("NodeKinds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var kinds = ((string Key, string Label)[])prop.GetValue(cut.Instance)!;

        Assert.Equal(4, kinds.Length);
    }

    [Fact]
    public async Task RebuildAndLoad_WhenNotInitialized_DoesNothing()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        // bUnit runs OnAfterRender, which imports the JS module and flips _initialized true.
        // Force the pre-init state to genuinely exercise the guard's early-return path.
        typeof(DockerRelationsGraph).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cut.Instance, false);

        var method = typeof(DockerRelationsGraph).GetMethod("RebuildAndLoad", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;
        // The guard returns before pushing data to the JS module - no setData call.
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "setData");
    }

    [Fact]
    public async Task ResetViewAsync_WhenNotInitialized_DoesNothing()
    {
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, MakeDockerData()));

        // bUnit runs OnAfterRender, which imports the JS module and flips _initialized true.
        // Force the pre-init state to genuinely exercise the guard's early-return path.
        typeof(DockerRelationsGraph).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(cut.Instance, false);

        var method = typeof(DockerRelationsGraph).GetMethod("ResetViewAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;
        // The guard returns before invoking the JS resetView.
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == "resetView");
    }

    [Fact]
    public void BuildData_ContainerWithProjectEdge()
    {
        var docker = new DockerDataDto
        {
            Containers = [new DockerContainerDto { ContainerId = "c1", Name = "web", Image = "nginx", State = "running", Project = "proj1" }],
            Images = [],
            Networks = [],
            Volumes = [],
            ComposeStacks = []
        };
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, docker));

        var method = typeof(DockerRelationsGraph).GetMethod("BuildData", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var json = (string)method.Invoke(cut.Instance, [])!;

        Assert.Contains("\"from\":\"p:proj1\"", json);
        Assert.Contains("\"to\":\"c:c1\"", json);
    }

    [Fact]
    public void BuildData_StoppedContainer_UsesStoppedGroup()
    {
        var docker = new DockerDataDto
        {
            Containers = [new DockerContainerDto { ContainerId = "c1", Name = "stopped-app", State = "exited" }],
            Images = [],
            Networks = [],
            Volumes = [],
            ComposeStacks = []
        };
        var cut = Render<DockerRelationsGraph>(p => p.Add(x => x.Docker, docker));

        var method = typeof(DockerRelationsGraph).GetMethod("BuildData", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var json = (string)method.Invoke(cut.Instance, [])!;

        Assert.Contains("\"group\":\"stopped\"", json);
    }
}
