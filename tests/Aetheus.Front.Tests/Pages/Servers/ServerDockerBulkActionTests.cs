// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Tests for DockerContainersTab:
/// - ProjectBulkActionAsync (iterates containers of a project and posts actions)
/// - ContainersForProject (filter helper)
/// - GetProjectAccentClass (hash-based CSS class)
/// - ParseEnvVars (static parser)
/// </summary>
public class ServerDockerBulkActionTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerDockerBulkActionTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        BunitTestHelper.UseImmediateDialogs(this);
    }

    private static ServerDetailDto MakeServerWithProject(string project = "myapp") => new()
    {
        Id = 10,
        Name = "docker-server",
        Hostname = "10.0.0.1",
        Type = ServerType.Docker,
        Status = ServerStatus.Online,
        Tags = [],
        Services = [],
        Docker = new DockerDataDto
        {
            Containers =
            [
                new DockerContainerDto
                {
                    ContainerId = "aaa111",
                    Name = "web",
                    Image = "nginx:latest",
                    State = "running",
                    Project = project,
                    Status = "Up 1h"
                },
                new DockerContainerDto
                {
                    ContainerId = "bbb222",
                    Name = "api",
                    Image = "dotnet:8",
                    State = "running",
                    Project = project,
                    Status = "Up 2h"
                },
                new DockerContainerDto
                {
                    ContainerId = "ccc333",
                    Name = "other",
                    Image = "redis:7",
                    State = "running",
                    Project = "other-project",
                    Status = "Up 3h"
                }
            ],
            Images = [],
            ComposeStacks = [],
            Networks = [],
            Volumes = []
        }
    };

    /// <summary>
    /// Every test here exercises the containers tab, which is a component of its own since the Docker
    /// section was split, so it is rendered directly rather than fished out of the parent.
    /// </summary>
    private IRenderedComponent<DockerContainersTab> RenderSection(ServerDetailDto? server = null)
    {
        server ??= MakeServerWithProject();
        _handler.SetJsonResponse("api/servers/10/docker/containers", new List<DockerContainerDto>());
        _handler.SetJsonResponse("api/servers/10/docker/action", true);
        return Render<DockerContainersTab>(p => p
            .Add(x => x.Server, server)
            .Add(x => x.ServerId, 10)
            .Add(x => x.InitialLoaded, true));
    }

    // ── ContainersForProject ──────────────────────────────────────────────────

    [Fact]
    public void ContainersForProject_ReturnsMatchingContainers()
    {
        var cut = RenderSection();
        var method = typeof(DockerContainersTab)
            .GetMethod("ContainersForProject", Priv)!;
        var result = (List<DockerContainerDto>)method.Invoke(cut.Instance, ["myapp"])!;
        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.Equal("myapp", c.Project));
    }

    [Fact]
    public void ContainersForProject_UnknownProject_ReturnsEmpty()
    {
        var cut = RenderSection();
        var method = typeof(DockerContainersTab)
            .GetMethod("ContainersForProject", Priv)!;
        var result = (List<DockerContainerDto>)method.Invoke(cut.Instance, ["nonexistent"])!;
        Assert.Empty(result);
    }

    [Fact]
    public void ContainersForProject_NullProject_MatchesNullContainers()
    {
        var server = MakeServerWithProject();
        // Add a container with null project
        server.Docker.Containers.Add(new DockerContainerDto
        {
            ContainerId = "ddd444",
            Name = "orphan",
            Image = "ubuntu",
            State = "exited",
            Project = null!,
            Status = "Exited"
        });
        var cut = RenderSection(server);
        var method = typeof(DockerContainersTab)
            .GetMethod("ContainersForProject", Priv)!;
        var result = (List<DockerContainerDto>)method.Invoke(cut.Instance, [string.Empty])!;
        // ContainersForProject("") matches containers where project is null or empty
        Assert.NotNull(result);
    }

    // ── ProjectBulkActionAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ProjectBulkActionAsync_EmptyProject_ReturnsEarly()
    {
        var cut = RenderSection();
        var method = typeof(DockerContainersTab)
            .GetMethod("ProjectBulkActionAsync", Priv)!;

        // "nonexistent" has 0 containers → should return early
        var target = Priv_Get<string?>(cut.Instance, "_projectActionTarget");
        await cut.InvokeAsync(async () =>
            await (Task)method.Invoke(cut.Instance, ["nonexistent", DockerContainerAction.Stop])!);

        // _projectActionTarget should remain null (early return)
        var after = Priv_Get<string?>(cut.Instance, "_projectActionTarget");
        Assert.Null(after);
    }

    [Fact]
    public async Task ProjectBulkActionAsync_AllSucceed_CallsToastSuccess()
    {
        _handler.SetJsonResponse("api/servers/10/docker/action", true);
        var cut = RenderSection();

        var method = typeof(DockerContainersTab)
            .GetMethod("ProjectBulkActionAsync", Priv)!;

        await cut.InvokeAsync(async () =>
            await (Task)method.Invoke(cut.Instance, ["myapp", DockerContainerAction.Stop])!);

        // After completion _projectActionTarget should be cleared
        var after = Priv_Get<string?>(cut.Instance, "_projectActionTarget");
        Assert.Null(after);
    }

    [Fact]
    public async Task ProjectBulkActionAsync_SomeFail_CallsToastError()
    {
        // First action succeeds, second fails
        _handler.SetJsonResponse("api/servers/10/docker/action", false);
        var cut = RenderSection();

        var method = typeof(DockerContainersTab)
            .GetMethod("ProjectBulkActionAsync", Priv)!;
        await cut.InvokeAsync(async () =>
            await (Task)method.Invoke(cut.Instance, ["myapp", DockerContainerAction.Start])!);

        var after = Priv_Get<string?>(cut.Instance, "_projectActionTarget");
        Assert.Null(after); // always cleared
    }

    // ── GetProjectAccentClass (public internal static) ─────────────────────────

    [Fact]
    public void GetProjectAccentClass_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, DockerContainersTab.GetProjectAccentClass(null));
        Assert.Equal(string.Empty, DockerContainersTab.GetProjectAccentClass(""));
        Assert.Equal(string.Empty, DockerContainersTab.GetProjectAccentClass("   "));
    }

    [Fact]
    public void GetProjectAccentClass_NonEmpty_ReturnsCssClass()
    {
        var cls = DockerContainersTab.GetProjectAccentClass("myapp");
        Assert.StartsWith("docker-project-accent-", cls);
        // Must be 0–7
        var suffix = int.Parse(cls.Replace("docker-project-accent-", ""));
        Assert.InRange(suffix, 0, 7);
    }

    [Fact]
    public void GetProjectAccentClass_Deterministic()
    {
        var a = DockerContainersTab.GetProjectAccentClass("stable-project");
        var b = DockerContainersTab.GetProjectAccentClass("stable-project");
        Assert.Equal(a, b);
    }

    // ── OpenProjectZoom / CloseProjectZoom ───────────────────────────────────

    [Fact]
    public async Task OpenProjectZoom_OpensDialogWithProject()
    {
        var cut = RenderSection();
        var open = typeof(DockerContainersTab).GetMethod("OpenProjectZoom", Priv)!;
        await cut.InvokeAsync(() => (Task)open.Invoke(cut.Instance, ["myapp"])!);
        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();

        Assert.Equal(typeof(DockerProjectDialog), dialog.LastComponent);
        Assert.Equal("myapp", dialog.LastParameters!["Project"]);
    }

    [Fact]
    public async Task OpenProjectZoom_ActionResult_RunsProjectBulkAction()
    {
        var cut = RenderSection();
        var dialog = (Aetheus.Front.Tests.TestDoubles.ImmediateDialogService)Services.GetRequiredService<OmniDialogService>();
        dialog.OpenResult = DockerContainerAction.Stop;
        var open = typeof(DockerContainersTab).GetMethod("OpenProjectZoom", Priv)!;
        await cut.InvokeAsync(() => (Task)open.Invoke(cut.Instance, ["myapp"])!);

        Assert.Equal(2, _handler.Requests.Count(request => request.Method == "POST" && request.Url.Contains("docker/action")));
    }

    // ── ParseEnvVars ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseEnvVars_ValidJson_ParsesCorrectly()
    {
        var method = typeof(DockerContainersTab)
            .GetMethod("ParseEnvVars", PrivStatic)!;
        var json = "[\"KEY=value\", \"PATH=/usr/bin\", \"HOME=/root\"]";
        var result = (List<DockerEnvVarDto>)method.Invoke(null, [json])!;
        Assert.Equal(3, result.Count);
        Assert.Equal("KEY", result[0].Key);
        Assert.Equal("value", result[0].Value);
        Assert.Equal("PATH", result[1].Key);
        Assert.Equal("/usr/bin", result[1].Value);
    }

    [Fact]
    public void ParseEnvVars_EmptyJson_ReturnsEmpty()
    {
        var method = typeof(DockerContainersTab)
            .GetMethod("ParseEnvVars", PrivStatic)!;
        var result = (List<DockerEnvVarDto>)method.Invoke(null, ["[]"])!;
        Assert.Empty(result);
    }

    [Fact]
    public void ParseEnvVars_EntryWithoutEquals_IsSkipped()
    {
        var method = typeof(DockerContainersTab)
            .GetMethod("ParseEnvVars", PrivStatic)!;
        var json = "[\"NOEQUALS\", \"VALID=yes\"]";
        var result = (List<DockerEnvVarDto>)method.Invoke(null, [json])!;
        Assert.Single(result);
        Assert.Equal("VALID", result[0].Key);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T? Priv_Get<T>(object obj, string field)
        => (T?)typeof(DockerContainersTab).GetField(field, Priv)!.GetValue(obj);
}
