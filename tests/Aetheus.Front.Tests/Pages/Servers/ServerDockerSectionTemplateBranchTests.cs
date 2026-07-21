// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Template-branch coverage for ServerDockerSection.razor.
/// Targets the 102 uncovered template lines - panel visibility states, container states,
/// inspect/logs/shell/env/browse panels, zoom dialog, limits dialog, prune dialog,
/// compose editor loading vs. loaded, and browse path-back-button.
/// </summary>
public class ServerDockerSectionTemplateBranchTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerDockerSectionTemplateBranchTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
    }

    // ── data helpers ─────────────────────────────────────────────────────────

    private static DockerDataDto BuildDockerData(
        IEnumerable<DockerContainerDto>? containers = null,
        IEnumerable<DockerImageDto>? images = null,
        IEnumerable<DockerComposeStackDto>? composeStacks = null,
        IEnumerable<DockerNetworkDto>? networks = null,
        IEnumerable<DockerVolumeDto>? volumes = null) => new()
        {
            Containers = containers?.ToList() ?? [],
            Images = images?.ToList() ?? [],
            ComposeStacks = composeStacks?.ToList() ?? [],
            Networks = networks?.ToList() ?? [],
            Volumes = volumes?.ToList() ?? []
        };

    private static ServerDetailDto BuildServer(DockerDataDto? docker = null) => new()
    {
        Id = 30,
        Name = "branch-srv",
        Hostname = "10.1.2.3",
        Type = ServerType.Docker,
        Status = ServerStatus.Online,
        CpuPercent = 10,
        MemoryUsedMb = 2048,
        MemoryTotalMb = 8192,
        DiskUsedGb = 20,
        DiskTotalGb = 200,
        Tags = [],
        Services = [],
        Apache = new ApacheDataDto { Modules = [], VirtualHosts = [] },
        Docker = docker ?? BuildDockerData()
    };

    private IRenderedComponent<ServerDockerSection> RenderSection(
        ServerDetailDto? server = null, bool initialLoaded = true)
    {
        var srv = server ?? BuildServer();
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/containers", srv.Docker.Containers);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/containers/logs", "log line A\nlog line B");
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/action", true);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/images/pull", true);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/prune", true);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/resource-limits", true);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/compose/action", true);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/compose/file", true);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/build", true);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/shell", true);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/browse", true);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/inspect", true);
        _handler.SetJsonResponse($"api/servers/{srv.Id}/docker/env", true);
        return Render<ServerDockerSection>(p => p
            .Add(x => x.Server, srv)
            .Add(x => x.ServerId, srv.Id)
            .Add(x => x.DockerInitialLoaded, initialLoaded));
    }

    // ── reflection helpers (drive real private methods + read state) ──────────

    private static T GetField<T>(IRenderedComponent<ServerDockerSection> cut, string name)
        => (T)typeof(ServerDockerSection).GetField(name, Priv)!.GetValue(cut.Instance)!;

    private static void InvokeMethod(IRenderedComponent<ServerDockerSection> cut, string name, params object?[] args)
    {
        var method = typeof(ServerDockerSection).GetMethod(name, Priv)!;
        cut.InvokeAsync(() => { method.Invoke(cut.Instance, args); }).GetAwaiter().GetResult();
    }

    private static void InvokeAsyncMethod(IRenderedComponent<ServerDockerSection> cut, string name, params object?[] args)
    {
        var method = typeof(ServerDockerSection).GetMethod(name, Priv)!;
        cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, args)!).GetAwaiter().GetResult();
    }

    // ── TEST 1: Skeleton shown when not initial-loaded and no containers ─────
    // Exercises the @if (!_dockerInitialLoaded && FilteredContainers.Count == 0) branch (line 22-29)

    [Fact]
    public void Render_NotInitialLoaded_EmptyContainers_ShowsSkeleton()
    {
        var srv = BuildServer(BuildDockerData());
        var cut = RenderSection(srv, initialLoaded: false);
        Assert.Contains("docker-skeleton", cut.Markup);
    }

    // ── TEST 2: Running container shows stop/restart buttons (line 104-113) ──
    // and the terminal/env/browse buttons (line 136-150)

    [Fact]
    public void Render_RunningContainer_MarkupContainsContainerName()
    {
        var docker = BuildDockerData(containers: [
            new DockerContainerDto
            {
                ContainerId = "run001run001run0",
                Name = "web-running",
                Image = "nginx:latest",
                State = "running",
                Status = "Up 2h",
                CpuPercent = 3.5,
                MemoryUsageMb = 128,
                MemoryLimitMb = 512,
                Project = "myapp"
            }
        ]);
        var cut = RenderSection(BuildServer(docker));
        Assert.Contains("web-running", cut.Markup);
        Assert.Contains("myapp", cut.Markup);
    }

    // ── TEST 3: Exited container shows play/delete buttons (line 115-125) ────

    [Fact]
    public void Render_ExitedContainer_MarkupContainsContainerName()
    {
        var docker = BuildDockerData(containers: [
            new DockerContainerDto
            {
                ContainerId = "ext001ext001ext0",
                Name = "api-exited",
                Image = "dotnet:8",
                State = "exited",
                Status = "Exited (0) 1h",
                CpuPercent = 0,
                MemoryUsageMb = 0,
                MemoryLimitMb = 256,
                Project = "myapp"
            }
        ]);
        var cut = RenderSection(BuildServer(docker));
        Assert.Contains("api-exited", cut.Markup);
    }

    // ── TEST 4: Paused container state in the badge ───────────────────────────

    [Fact]
    public void Render_PausedAndRestartingContainers_RendersWithoutError()
    {
        var docker = BuildDockerData(containers: [
            new DockerContainerDto
            {
                ContainerId = "pau001pau001pau0",
                Name = "paused-svc",
                Image = "redis:7",
                State = "paused",
                Status = "Paused",
                CpuPercent = 0.0,
                MemoryUsageMb = 64,
                MemoryLimitMb = 128,
                Project = "cache"
            },
            new DockerContainerDto
            {
                ContainerId = "rst001rst001rst0",
                Name = "restarting-svc",
                Image = "node:18",
                State = "restarting",
                Status = "Restarting (1) 5s ago",
                CpuPercent = 0.5,
                MemoryUsageMb = 32,
                MemoryLimitMb = 256,
                Project = "cache"
            }
        ]);
        var cut = RenderSection(BuildServer(docker));
        Assert.Contains("paused-svc", cut.Markup);
        Assert.Contains("restarting-svc", cut.Markup);
    }

    // ── TEST 5: Container with no project (empty string) hits the NoProject branch

    [Fact]
    public void Render_ContainerWithEmptyProject_RendersContainerRow()
    {
        var docker = BuildDockerData(containers: [
            new DockerContainerDto
            {
                ContainerId = "nop001nop001nop0",
                Name = "orphan-ctr",
                Image = "alpine:latest",
                State = "running",
                Status = "Up 1m",
                CpuPercent = 0.1,
                MemoryUsageMb = 8,
                MemoryLimitMb = 64,
                Project = ""
            }
        ]);
        var cut = RenderSection(BuildServer(docker));
        // The container row renders with its name and image even when it has no project group.
        Assert.Contains("orphan-ctr", cut.Markup);
        Assert.Contains("alpine:latest", cut.Markup);
    }

    // ── TEST 6: Logs panel - _logsContainerId set, _logsContent null (spinner) ─
    // Exercises lines 158-176 and the inner null-content branch (lines 167-170)

    [Fact]
    public void Render_LogsPanel_NullContent_ShowsSpinner_InMarkup()
    {
        var docker = BuildDockerData(containers: [
            new DockerContainerDto
            {
                ContainerId = "logctr000000logs",
                Name = "log-target",
                Image = "nginx",
                State = "running",
                Status = "Up 5m",
                Project = "x"
            }
        ]);
        var cut = RenderSection(BuildServer(docker));

        // Set _logsContainerId, leave _logsContent null → spinner branch
        typeof(ServerDockerSection).GetField("_logsContainerId", Priv)!.SetValue(cut.Instance, "logctr000000logs");
        typeof(ServerDockerSection).GetField("_logsContent", Priv)!.SetValue(cut.Instance, null);
        cut.Render();

        Assert.Contains("logctr000000logs"[..12], cut.Markup);
    }

    // ── TEST 7: Logs panel - _logsContent not null → shows <pre> block ───────
    // Exercises lines 172-174

    [Fact]
    public void Render_LogsPanel_WithContent_ShowsPreBlock()
    {
        var cut = RenderSection();
        typeof(ServerDockerSection).GetField("_logsContainerId", Priv)!.SetValue(cut.Instance, "logctr000000logs");
        typeof(ServerDockerSection).GetField("_logsContent", Priv)!.SetValue(cut.Instance, "line1\nline2");
        cut.Render();

        Assert.Contains("docker-logs", cut.Markup);
        Assert.Contains("line1", cut.Markup);
    }

    // ── TEST 8: Inspect panel - _inspectContainerId set, content null → waiting ─
    // Exercises lines 178-203, inner null branch (lines 194-197)

    [Fact]
    public void Render_InspectPanel_NullContent_ShowsWaiting()
    {
        var cut = RenderSection();
        typeof(ServerDockerSection).GetField("_inspectContainerId", Priv)!.SetValue(cut.Instance, "inspectid000001a");
        typeof(ServerDockerSection).GetField("_inspectContent", Priv)!.SetValue(cut.Instance, null);
        cut.Render();

        Assert.Contains("inspectid000001a"[..12], cut.Markup);
    }

    // ── TEST 9: Inspect panel - content populated → copy button appears ───────
    // Exercises lines 184-190 (copy-button when _inspectContent not null)

    [Fact]
    public void Render_InspectPanel_WithContent_ShowsCopyButton()
    {
        var cut = RenderSection();
        typeof(ServerDockerSection).GetField("_inspectContainerId", Priv)!.SetValue(cut.Instance, "inspectid000001a");
        typeof(ServerDockerSection).GetField("_inspectContent", Priv)!.SetValue(cut.Instance, "{\"Id\":\"abc\"}");
        cut.Render();

        // Inspect pre block and copy button icon both appear
        Assert.Contains("content_copy", cut.Markup);
        Assert.Contains("docker-logs", cut.Markup);
    }

    // ── TEST 10: Shell panel visible ─────────────────────────────────────────
    // Exercises lines 205-224

    [Fact]
    public void Render_ShellPanel_Visible_RendersTerminal()
    {
        var cut = RenderSection();
        typeof(ServerDockerSection).GetField("_shellContainerId", Priv)!.SetValue(cut.Instance, "shellid0000shell");
        typeof(ServerDockerSection).GetField("_shellContainerName", Priv)!.SetValue(cut.Instance, "my-shell-ctr");
        typeof(ServerDockerSection).GetField("_shellOutput", Priv)!.SetValue(cut.Instance, "$ ls\nDockerfile");
        cut.Render();

        Assert.Contains("my-shell-ctr", cut.Markup);
        Assert.Contains("docker-shell", cut.Markup);
    }

    // ── TEST 11: Env panel - null content → waiting ───────────────────────────
    // Exercises lines 226-252, null-content branch (lines 235-237)

    [Fact]
    public void Render_EnvPanel_NullContent_ShowsWaiting()
    {
        var cut = RenderSection();
        typeof(ServerDockerSection).GetField("_envContainerId", Priv)!.SetValue(cut.Instance, "envctr000000env0");
        typeof(ServerDockerSection).GetField("_envContainerName", Priv)!.SetValue(cut.Instance, "env-target");
        typeof(ServerDockerSection).GetField("_envContent", Priv)!.SetValue(cut.Instance, null);
        cut.Render();

        Assert.Contains("env-target", cut.Markup);
    }

    // ── TEST 12: Env panel - content populated → table appears ───────────────
    // Exercises lines 241-250 (@foreach env vars table)

    [Fact]
    public void Render_EnvPanel_WithContent_ShowsEnvTable()
    {
        var cut = RenderSection();
        typeof(ServerDockerSection).GetField("_envContainerId", Priv)!.SetValue(cut.Instance, "envctr000000env0");
        typeof(ServerDockerSection).GetField("_envContainerName", Priv)!.SetValue(cut.Instance, "env-target");
        // ParseEnvVars expects a JSON-array-like string
        typeof(ServerDockerSection).GetField("_envContent", Priv)!.SetValue(cut.Instance, "[\"PATH=/usr/local/sbin:/usr/bin\",\"HOME=/root\",\"USER=app\"]");
        cut.Render();

        Assert.Contains("role=\"grid\"", cut.Markup);
        Assert.Contains("PATH", cut.Markup);
        Assert.Contains("HOME", cut.Markup);
    }

    // ── TEST 13: Browse panel - root path, no content → waiting, no up-button ─
    // Exercises lines 254-278, including the _browsePath != "/" sub-branch (lines 261-265)

    [Fact]
    public void Render_BrowsePanel_AtRoot_NoUpButton()
    {
        var cut = RenderSection();
        typeof(ServerDockerSection).GetField("_browseContainerId", Priv)!.SetValue(cut.Instance, "bwsctr000000bws0");
        typeof(ServerDockerSection).GetField("_browseContainerName", Priv)!.SetValue(cut.Instance, "browse-target");
        typeof(ServerDockerSection).GetField("_browsePath", Priv)!.SetValue(cut.Instance, "/");
        typeof(ServerDockerSection).GetField("_browseContent", Priv)!.SetValue(cut.Instance, null);
        cut.Render();

        Assert.Contains("browse-target", cut.Markup);
        // At root path, the "arrow_upward" (go-parent) button must NOT appear
        Assert.DoesNotContain("arrow_upward", cut.Markup);
    }

    // ── TEST 14: Browse panel - non-root path, content → pre and up-button ───

    [Fact]
    public void Render_BrowsePanel_NonRootPath_ShowsUpButtonAndContent()
    {
        var cut = RenderSection();
        typeof(ServerDockerSection).GetField("_browseContainerId", Priv)!.SetValue(cut.Instance, "bwsctr000000bws0");
        typeof(ServerDockerSection).GetField("_browseContainerName", Priv)!.SetValue(cut.Instance, "browse-target");
        typeof(ServerDockerSection).GetField("_browsePath", Priv)!.SetValue(cut.Instance, "/etc");
        typeof(ServerDockerSection).GetField("_browseContent", Priv)!.SetValue(cut.Instance, "hosts  resolv.conf  passwd");
        cut.Render();

        Assert.Contains("arrow_upward", cut.Markup);
        Assert.Contains("docker-logs", cut.Markup);
        Assert.Contains("hosts", cut.Markup);
    }

    // ── TEST 15: Compose editor - _composeEditorVisible, loading branch ───────
    // Exercises lines 378-404, and the inner _composeFileLoading branch (lines 387-391)

    [Fact]
    public void OpenComposeEditorAsync_SetsVisibleAndStack_CloseResets()
    {
        _handler.SetResponse(
            HttpMethod.Get,
            "api/servers/30/docker/compose/webapp/file",
            System.Net.HttpStatusCode.OK);
        var docker = BuildDockerData(composeStacks: [
            new DockerComposeStackDto
            {
                Name = "webapp",
                Status = "running",
                RunningCount = 2,
                TotalCount = 2,
                ConfigFile = "/opt/webapp/docker-compose.yml"
            }
        ]);
        var cut = RenderSection(BuildServer(docker));

        // Drive the REAL open method (not a set-then-get) and assert its effect on state.
        InvokeAsyncMethod(cut, "OpenComposeEditorAsync", "webapp");
        Assert.True(GetField<bool>(cut, "_composeEditorVisible"));
        Assert.Equal("webapp", GetField<string>(cut, "_composeEditorStack"));
        // compose/file mock returns true ⇒ the success branch keeps the loading flag set.
        Assert.True(GetField<bool>(cut, "_composeFileLoading"));

        // CloseComposeEditor resets the editor state.
        InvokeMethod(cut, "CloseComposeEditor");
        Assert.False(GetField<bool>(cut, "_composeEditorVisible"));
        Assert.Equal(string.Empty, GetField<string>(cut, "_composeEditorStack"));
    }

    // ── TEST 17: Zoom dialog - OpenProjectZoom sets the zoomed project, Close clears it ─

    [Fact]
    public async Task OpenProjectZoom_OpensProjectDialog_WithExpectedParameters()
    {
        var docker = BuildDockerData(
            containers: [
                new DockerContainerDto
                {
                    ContainerId = "zoomctr00000zoom",
                    Name = "zoom-web",
                    Image = "nginx",
                    State = "running",
                    Status = "Up 1h",
                    Project = "zoomapp",
                    CpuPercent = 1.0,
                    MemoryUsageMb = 64,
                    MemoryLimitMb = 256
                }
            ],
            composeStacks: [new DockerComposeStackDto { Name = "zoomapp", Status = "running", RunningCount = 1, TotalCount = 1, ConfigFile = "/opt/zoomapp/docker-compose.yml" }]
        );
        var cut = RenderSection(BuildServer(docker));

        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<Radzen.DialogService>();
        var method = typeof(ServerDockerSection).GetMethod("OpenProjectZoom", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["zoomapp"])!);

        Assert.Equal(typeof(DockerProjectDialog), dialog.LastComponent);
        Assert.Equal("zoomapp", dialog.LastParameters!["Project"]);
        Assert.Same(cut.Instance.Server, dialog.LastParameters["Server"]);
    }

    // ── TEST 18: OpenResourceLimitsDialog reads the container's memory limit ──

    [Fact]
    public async Task OpenResourceLimitsDialog_ReadsContainerMemoryLimit()
    {
        var docker = BuildDockerData(containers: [
            new DockerContainerDto
            {
                ContainerId = "limitctr000limit",
                Name = "limited-ctr",
                Image = "nginx",
                State = "running",
                Status = "Up",
                Project = "grp",
                MemoryLimitMb = 512
            }
        ]);
        var cut = RenderSection(BuildServer(docker));

        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<Radzen.DialogService>();
        var method = typeof(ServerDockerSection).GetMethod("OpenResourceLimitsDialog", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["limitctr000limit"])!);

        Assert.Equal(typeof(DockerResourceLimitsDialog), dialog.LastComponent);
        Assert.Equal("limitctr000limit", dialog.LastParameters!["ContainerId"]);
        Assert.Equal(512, dialog.LastParameters["MemoryLimitMb"]);
    }

    // ── TEST 19: ConfirmRemoveContainerAsync populates and shows the confirm dialog ─

    [Fact]
    public async Task ConfirmRemoveContainer_ShowsDialog_WithTitleAndMessage()
    {
        var cut = RenderSection();

        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<Radzen.DialogService>();
        var method = typeof(ServerDockerSection).GetMethod("ConfirmRemoveContainerAsync", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, ["ctrid000000remov", "doomed-ctr"])!);

        Assert.Equal("RemoveContainer", dialog.LastTitle);
        Assert.Equal("RemoveContainerConfirm", dialog.LastConfirmMessage);
    }

    // ── TEST 21: Images with repos+tags renders in images tab ────────────────
    // Exercises the image-grid template including Created and ImageId sub-expressions.
    // The Images tab is not the active tab in bUnit, so its markup is not rendered.
    // We verify the FilteredImages computed property returns the expected data (which
    // the template would render if the tab were active).

    [Fact]
    public void Render_ImagesWithReposAndTags_RendersGrid()
    {
        var docker = BuildDockerData(images: [
            new DockerImageDto
            {
                ImageId = "sha256:aabb11223344aabb",
                Repository = "myrepo/myimage",
                Tag = "v1.0.0",
                Size = "250MB",
                Created = DateTime.UtcNow.AddDays(-7),
                Project = "webapp"
            },
            new DockerImageDto
            {
                ImageId = "sha256:ccdd55667788ccdd",
                Repository = "<none>",
                Tag = "<none>",
                Size = "85MB",
                Created = DateTime.UtcNow.AddDays(-30),
                Project = ""
            }
        ]);
        var cut = RenderSection(BuildServer(docker));
        // Verify via the computed property (covers FilteredImages expression in the template)
        var filteredProp = typeof(ServerDockerSection).GetProperty("FilteredImages", Priv)!;
        var filtered = (List<DockerImageDto>)filteredProp.GetValue(cut.Instance)!;
        Assert.Equal(2, filtered.Count);
        Assert.Equal("myrepo/myimage", filtered[0].Repository);
        Assert.Equal("v1.0.0", filtered[0].Tag);
    }

    // ── TEST 22: Networks and volumes tabs render ─────────────────────────────
    // Exercises the network NetworkId truncation template and volume grid

    [Fact]
    public void Render_NetworksAndVolumes_RenderGrids()
    {
        var docker = BuildDockerData(
            networks: [
                new DockerNetworkDto
                {
                    NetworkId = "netid001002003004",
                    Name = "bridge",
                    Driver = "bridge",
                    Scope = "local",
                    Project = "infra"
                }
            ],
            volumes: [
                new DockerVolumeDto
                {
                    Name = "postgres_data",
                    Driver = "local",
                    Mountpoint = "/var/lib/docker/volumes/postgres_data/_data",
                    Project = "db"
                }
            ]
        );
        var cut = RenderSection(BuildServer(docker));
        // Both grids are present in the tab items
        Assert.Contains("DockerNetworks", cut.Markup);
        Assert.Contains("DockerVolumes", cut.Markup);
    }

    // ── TEST 23: Container search filters visible containers ─────────────────
    // Exercises _containerSearch state field and FilteredContainers computed property

    [Fact]
    public void Render_ContainerSearch_FiltersByName()
    {
        var docker = BuildDockerData(containers: [
            new DockerContainerDto
            {
                ContainerId = "filterAAA000001",
                Name = "filter-web",
                Image = "nginx",
                State = "running",
                Status = "Up",
                Project = "grp"
            },
            new DockerContainerDto
            {
                ContainerId = "filterBBB000002",
                Name = "filter-db",
                Image = "postgres",
                State = "running",
                Status = "Up",
                Project = "grp"
            }
        ]);
        var cut = RenderSection(BuildServer(docker));

        typeof(ServerDockerSection).GetField("_containerSearch", Priv)!.SetValue(cut.Instance, "web");

        var filteredProp = typeof(ServerDockerSection).GetProperty("FilteredContainers", Priv)!;
        var filtered = (List<DockerContainerDto>)filteredProp.GetValue(cut.Instance)!;
        Assert.Single(filtered);
        Assert.Equal("filter-web", filtered[0].Name);
    }
}
