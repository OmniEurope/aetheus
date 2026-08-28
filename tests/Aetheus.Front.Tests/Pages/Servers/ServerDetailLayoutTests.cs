// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Layout;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Covers the action-button / edit-panel / agent-update chrome that moved out of the retired
/// monolithic <c>ServerDetail.razor</c> into <see cref="ServerDetailLayout"/> (audit 360 lot G).
/// The layout owns: Contact/Update/Edit/Delete buttons (gated by <c>_canWrite</c>), the inline
/// edit panel (Save → <c>UpdateServerAsync</c>, Delete → <c>DeleteServerAsync</c>), and the
/// <c>AgentUpdateProgressCard</c> wired to the shared loader hub.
/// </summary>
public class ServerDetailLayoutTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerDetailLayoutTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        // The layout @injects the scoped loader; the production DI registers it scoped too.
        Services.AddScoped(sp => new ServerDetailLoader(
            sp.GetRequiredService<ApiClient>(),
            sp.GetRequiredService<HubConnectionFactory>(),
            sp.GetRequiredService<NavigationManager>(),
            NullLogger<ServerDetailLoader>.Instance));
    }

    private static ServerDetailDto MakeServer(int id = 1, string name = "srv-1",
        ServerStatus status = ServerStatus.Online, string agentVersion = "3.0.0") => new()
        {
            Id = id,
            Name = name,
            Hostname = "10.0.0.1",
            IpAddress = "10.0.0.1",
            Type = ServerType.Normal,
            Status = status,
            CpuPercent = 10,
            MemoryUsedMb = 2048,
            MemoryTotalMb = 8192,
            DiskUsedGb = 10,
            DiskTotalGb = 100,
            AgentVersion = agentVersion,
            Tags = ["prod"],
            Services = [],
            Docker = new DockerDataDto { Containers = [], Images = [], ComposeStacks = [], Networks = [], Volumes = [] },
            Apache = new ApacheDataDto(),
            Certbot = new CertbotDataDto(),
            Cron = new CronDataDto(),
            Mail = new MailDataDto(),
            Teamspeak = new TeamspeakDataDto(),
            Portsentry = new PortsentryDataDto(),
            Rkhunter = new RkhunterDataDto()
        };

    /// <summary>Seeds the scoped loader with a server so the layout renders the loaded branch.</summary>
    private ServerDetailLoader SeedLoader(ServerDetailDto server)
    {
        var loader = Services.GetRequiredService<ServerDetailLoader>();
        typeof(ServerDetailLoader).GetProperty(nameof(ServerDetailLoader.Server))!.SetValue(loader, server);
        typeof(ServerDetailLoader).GetProperty(nameof(ServerDetailLoader.InitialLoadCompleted))!.SetValue(loader, true);
        return loader;
    }

    private IRenderedComponent<ServerDetailLayout> RenderLayout(ServerDetailDto server)
    {
        SeedLoader(server);
        return Render<ServerDetailLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<div id=\"body\">body</div>")));
    }

    // ── action buttons gated by _canWrite ─────────────────────────────────────

    [Fact]
    public void ActionButtons_Rendered_WhenCanWrite()
    {
        var cut = RenderLayout(MakeServer());
        // Default test permissions grant Read+Write → all four action buttons present.
        Assert.Contains("ContactAgent", cut.Markup);
        Assert.Contains("UpdateAgent", cut.Markup);
        Assert.Contains("Edit", cut.Markup);
        Assert.Contains("Delete", cut.Markup);
        Assert.Contains("aria-label=\"ContactAgent\"", cut.Markup);
        Assert.Contains("aria-label=\"UpdateAgent\"", cut.Markup);
        Assert.Contains("aria-label=\"Edit\"", cut.Markup);
        Assert.Contains("aria-label=\"Delete\"", cut.Markup);
    }

    [Fact]
    public void ActionButtons_Hidden_WhenReadOnly()
    {
        // Re-register services WITHOUT write permission by clearing the permission service.
        var perms = Services.GetRequiredService<PermissionService>();
        perms.SetPermissions([], isAdmin: false);

        var cut = RenderLayout(MakeServer());
        var canWrite = (bool)typeof(ServerDetailLayout).GetField("_canWrite", Priv)!.GetValue(cut.Instance)!;
        Assert.False(canWrite);
        Assert.DoesNotContain("EditServer", cut.Markup);
    }

    // ── edit panel ────────────────────────────────────────────────────────────

    [Fact]
    public void OpenEditDialog_PopulatesFields_AndShowsPanel()
    {
        var cut = RenderLayout(MakeServer(name: "edit-me") with { Tags = ["a", "b"] });

        var method = typeof(ServerDetailLayout).GetMethod("OpenEditDialog", Priv)!;
        cut.InvokeAsync(() => method.Invoke(cut.Instance, []));

        Assert.True((bool)typeof(ServerDetailLayout).GetField("_editVisible", Priv)!.GetValue(cut.Instance)!);
        Assert.Equal("edit-me", typeof(ServerDetailLayout).GetField("_editName", Priv)!.GetValue(cut.Instance));
        cut.Render();
        Assert.Contains("EditServer", cut.Markup);
    }

    [Fact]
    public void OpenEditDialog_ServerNull_NoOp()
    {
        var loader = Services.GetRequiredService<ServerDetailLoader>();
        typeof(ServerDetailLoader).GetProperty(nameof(ServerDetailLoader.InitialLoadCompleted))!.SetValue(loader, true);
        var cut = Render<ServerDetailLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<div/>")));

        var method = typeof(ServerDetailLayout).GetMethod("OpenEditDialog", Priv)!;
        cut.InvokeAsync(() => method.Invoke(cut.Instance, []));

        Assert.False((bool)typeof(ServerDetailLayout).GetField("_editVisible", Priv)!.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task SaveEditAsync_Success_CallsUpdateServerAndUpdatesLoader()
    {
        var server = MakeServer(7, "before");
        var loader = SeedLoader(server);
        _handler.SetJsonResponse("api/servers/7", server with { Name = "after", Tags = ["x"] });

        var cut = Render<ServerDetailLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<div/>")));

        typeof(ServerDetailLayout).GetField("_editName", Priv)!.SetValue(cut.Instance, "after");
        typeof(ServerDetailLayout).GetField("_editTags", Priv)!.SetValue(cut.Instance, "x");
        typeof(ServerDetailLayout).GetField("_editVisible", Priv)!.SetValue(cut.Instance, true);

        var method = typeof(ServerDetailLayout).GetMethod("SaveEditAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.False((bool)typeof(ServerDetailLayout).GetField("_editSaving", Priv)!.GetValue(cut.Instance)!);
        Assert.False((bool)typeof(ServerDetailLayout).GetField("_editVisible", Priv)!.GetValue(cut.Instance)!);
        // Loader's server was mutated via UpdateServerFields.
        Assert.Equal("after", loader.Server!.Name);
    }

    [Fact]
    public async Task SaveEditAsync_ApiFails_KeepsPanelAndClearsSaving()
    {
        var server = MakeServer(8, "stay");
        SeedLoader(server);
        _handler.SetResponse("api/servers/8", System.Net.HttpStatusCode.BadRequest);

        var cut = Render<ServerDetailLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<div/>")));

        typeof(ServerDetailLayout).GetField("_editName", Priv)!.SetValue(cut.Instance, "stay");
        typeof(ServerDetailLayout).GetField("_editTags", Priv)!.SetValue(cut.Instance, "");
        typeof(ServerDetailLayout).GetField("_editVisible", Priv)!.SetValue(cut.Instance, true);

        var method = typeof(ServerDetailLayout).GetMethod("SaveEditAsync", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        Assert.False((bool)typeof(ServerDetailLayout).GetField("_editSaving", Priv)!.GetValue(cut.Instance)!);
    }

    // ── delete ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnDeleteServer_ServerNull_ReturnsEarly()
    {
        var loader = Services.GetRequiredService<ServerDetailLoader>();
        typeof(ServerDetailLoader).GetProperty(nameof(ServerDetailLoader.InitialLoadCompleted))!.SetValue(loader, true);
        var cut = Render<ServerDetailLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<div/>")));

        var method = typeof(ServerDetailLayout).GetMethod("OnDeleteServer", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // Null server short-circuits before the confirm/delete flow - no DELETE is ever sent.
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "DELETE");
    }

    [Fact]
    public async Task OnDeleteServer_ShowsBusyStateWhileDeleteCompletes()
    {
        var releaseDelete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.SetAsyncJsonResponse(HttpMethod.Delete, "api/servers/12", async ct =>
        {
            await releaseDelete.Task.WaitAsync(ct);
            return new { };
        });
        var cut = RenderLayout(MakeServer(12, "delete-me"));

        var deletion = cut.InvokeAsync(cut.Instance.DeleteServerConfirmedAsync);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Deleting", cut.Markup);
        });
        Assert.False(deletion.IsCompleted);

        releaseDelete.SetResult();
        await deletion;
        cut.WaitForAssertion(() => Assert.DoesNotContain("Deleting", cut.Markup));
    }

    // ── contact agent dialog ────────────────────────────────────────────────

    [Fact]
    public void OpenContactAgentDialog_ServerLoaded_OpensDialog()
    {
        var cut = RenderLayout(MakeServer(9, "contact-me"));
        var dialog = Services.GetRequiredService<Radzen.DialogService>();
        var opened = false;
        dialog.OnOpen += (_, _, _, _) => opened = true;

        var method = typeof(ServerDetailLayout).GetMethod("OpenContactAgentDialog", Priv)!;
        // Returns a non-completing Task (dialog stays open); the loaded server drives DialogService.Open.
        _ = (Task)method.Invoke(cut.Instance, [])!;
        Assert.True(opened);
    }

    [Fact]
    public void OpenContactAgentDialog_ServerNull_ReturnsCompletedTask()
    {
        var loader = Services.GetRequiredService<ServerDetailLoader>();
        typeof(ServerDetailLoader).GetProperty(nameof(ServerDetailLoader.InitialLoadCompleted))!.SetValue(loader, true);
        var cut = Render<ServerDetailLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<div/>")));

        var method = typeof(ServerDetailLayout).GetMethod("OpenContactAgentDialog", Priv)!;
        var task = (Task)method.Invoke(cut.Instance, [])!;
        Assert.True(task.IsCompleted);
    }

    // ── AgentUpdateProgressCard presence + heartbeat wiring ───────────────────

    [Fact]
    public void AgentUpdateProgressCard_IsPresent_WhenServerLoaded()
    {
        var cut = RenderLayout(MakeServer());
        var card = typeof(ServerDetailLayout)
            .GetField("_agentUpdateProgress", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(card);
    }

    [Fact]
    public void OnLoaderChanged_RecomputesCanWrite_NoThrow()
    {
        var loader = SeedLoader(MakeServer(11, "load-me"));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/servers/11/overview");
        var cut = Render<ServerDetailLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<div/>")));

        // Fire the loader event (as a heartbeat would) - recomputes _canWrite + pokes the card.
        var onChanged = typeof(ServerDetailLayout).GetMethod("OnLoaderChanged", Priv)!;
        cut.InvokeAsync(() => onChanged.Invoke(cut.Instance, []));

        Assert.True((bool)typeof(ServerDetailLayout).GetField("_canWrite", Priv)!.GetValue(cut.Instance)!);
        var owner = Services.GetRequiredService<BreadcrumbService>().Items[1];
        Assert.Equal("load-me", owner.Text);
        Assert.False(owner.IsLoading);
        GC.KeepAlive(loader);
    }

    // ── header chrome ─────────────────────────────────────────────────────────

    [Fact]
    public void Header_ShowsServerName_AndOnlineBadge()
    {
        var cut = RenderLayout(MakeServer(name: "header-srv"));
        Assert.Contains("header-srv", cut.Markup);
        Assert.Contains("Online", cut.Markup);
    }

    [Fact]
    public void Header_OfflineServer_ShowsOfflineBadge()
    {
        var cut = RenderLayout(MakeServer(name: "down-srv", status: ServerStatus.Offline) with
        {
            LastHeartbeat = DateTime.Now.AddMinutes(-60)
        });
        Assert.Contains("down-srv", cut.Markup);
        Assert.Contains("Offline", cut.Markup);
    }

    [Fact]
    public void CompatibilityDetails_AreNotRendered_WhenAgentIsUpToDate()
    {
        var cut = RenderLayout(MakeServer() with
        {
            AgentCompatibility = new AgentCompatibilityDto
            {
                Status = AgentCompatibilityStatus.UpToDate,
                Reason = AgentCompatibilityReason.Current,
                InstalledVersion = "1.0.1294",
                TargetVersion = "1.0.1294",
                AgentProtocolVersion = 2,
                MinimumSupportedProtocol = 1,
                MaximumSupportedProtocol = 2
            }
        });

        Assert.DoesNotContain("AgentCompatibilityDetails", cut.Markup);
    }

    [Fact]
    public void CompatibilityDetails_AreNotRendered_WhenAgentRequiresUpdate()
    {
        var cut = RenderLayout(MakeServer() with
        {
            AgentCompatibility = new AgentCompatibilityDto
            {
                Status = AgentCompatibilityStatus.UpdateRequired,
                Reason = AgentCompatibilityReason.UnsupportedProtocol,
                InstalledVersion = "1.0.1200",
                TargetVersion = "1.0.1294",
                AgentProtocolVersion = 0,
                MinimumSupportedProtocol = 1,
                MaximumSupportedProtocol = 2
            }
        });

        Assert.DoesNotContain("AgentCompatibilityDetails", cut.Markup);
        Assert.DoesNotContain("AgentCompatibilityReason_UnsupportedProtocol", cut.Markup);
        Assert.DoesNotContain("1.0.1200", cut.Markup);
    }

    [Fact]
    public void Body_Rendered_WhenServerLoaded()
    {
        var cut = RenderLayout(MakeServer());
        Assert.Contains("id=\"body\"", cut.Markup);
    }

    [Fact]
    public void NotFoundState_Rendered_WhenServerNullAfterLoad()
    {
        var loader = Services.GetRequiredService<ServerDetailLoader>();
        typeof(ServerDetailLoader).GetProperty(nameof(ServerDetailLoader.InitialLoadCompleted))!.SetValue(loader, true);
        var cut = Render<ServerDetailLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<div/>")));
        Assert.Contains("ServerNotFound", cut.Markup);
    }
}
