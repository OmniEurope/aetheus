// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ServersPage = Aetheus.Front.Pages.Servers.Servers;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServersPageExtendedTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    private static readonly BindingFlags InstPriv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static PaginatedResult<ServerDto> TwoServers() => new()
    {
        Items =
        [
            new ServerDto { Id = 1, Name = "web-01", Hostname = "10.0.0.1", Type = ServerType.Docker,  Status = ServerStatus.Online,  Tags = ["prod"] },
            new ServerDto { Id = 2, Name = "db-01",  Hostname = "10.0.0.2", Type = ServerType.Normal,  Status = ServerStatus.Offline, Tags = ["prod", "db"] }
        ],
        TotalCount = 2
    };

    public ServersPageExtendedTests() => _handler = BunitTestHelper.RegisterServices(this);

    // ── IsColumnVisible ──────────────────────────────────────────────────────

    [Fact]
    public void IsColumnVisible_KnownColumn_DefaultsToTrue()
    {
        _handler.SetJsonResponse("api/servers", TwoServers());
        var cut = Render<ServersPage>();

        // All toggleable columns default to visible.
        foreach (var col in new[] { "Type", "Hostname", "OsDescription", "AgentVersion", "Tags", "LastHeartbeat" })
            Assert.True(cut.Instance.IsColumnVisible(col), $"Column '{col}' should be visible by default");
    }

    [Fact]
    public void IsColumnVisible_UnknownColumn_ReturnsTrue()
    {
        _handler.SetJsonResponse("api/servers", TwoServers());
        var cut = Render<ServersPage>();

        // Columns not in the dictionary are treated as visible.
        Assert.True(cut.Instance.IsColumnVisible("NonExistent"));
    }

    [Fact]
    public async Task IsColumnVisible_AfterHidden_ReturnsFalse()
    {
        _handler.SetJsonResponse("api/servers", TwoServers());
        var cut = Render<ServersPage>();

        var method = typeof(ServersPage).GetMethod("OnColumnVisibilityChanged", InstPriv)!;
        await cut.InvokeAsync(async () =>
            await (Task)method.Invoke(cut.Instance, ["Tags", false])!);

        Assert.False(cut.Instance.IsColumnVisible("Tags"));
    }

    [Fact]
    public async Task IsColumnVisible_AfterToggleChooser_StateChanges()
    {
        _handler.SetJsonResponse("api/servers", TwoServers());
        var cut = Render<ServersPage>();

        var toggle = typeof(ServersPage).GetMethod("ToggleColumnChooser", InstPriv)!;
        toggle.Invoke(cut.Instance, null);

        var showChooser = (bool)typeof(ServersPage)
            .GetField("_showColumnChooser", InstPriv)!
            .GetValue(cut.Instance)!;
        Assert.True(showChooser);

        // Second toggle hides it again.
        toggle.Invoke(cut.Instance, null);
        showChooser = (bool)typeof(ServersPage)
            .GetField("_showColumnChooser", InstPriv)!
            .GetValue(cut.Instance)!;
        Assert.False(showChooser);
    }

    // ── ClearFilters ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearFilters_ResetsAllFilters()
    {
        _handler.SetJsonResponse("api/servers", TwoServers());
        var cut = Render<ServersPage>();

        cut.Instance._search = "prod";
        cut.Instance._typeFilter = ServerType.Docker;
        cut.Instance._statusFilter = ServerStatus.Online;

        await cut.Instance.ClearFilters();

        Assert.Null(cut.Instance._search);
        Assert.Null(cut.Instance._typeFilter);
        Assert.Null(cut.Instance._statusFilter);
    }

    // ── FilterByTag ───────────────────────────────────────────────────────────

    [Fact]
    public async Task FilterByTag_SetsSearchToTag()
    {
        _handler.SetJsonResponse("api/servers", TwoServers());
        var cut = Render<ServersPage>();

        await cut.Instance.FilterByTag("prod");

        Assert.Equal("prod", cut.Instance._search);
    }

    [Fact]
    public async Task FilterByTag_OverwritesPreviousSearch()
    {
        _handler.SetJsonResponse("api/servers", TwoServers());
        var cut = Render<ServersPage>();

        await cut.Instance.FilterByTag("first");
        await cut.Instance.FilterByTag("second");

        Assert.Equal("second", cut.Instance._search);
    }

    // ── OnDeleteServer ────────────────────────────────────────────────────────

    [Fact]
    public async Task OnDeleteServer_HidesRowBeforeDeleteCompletes()
    {
        var releaseDelete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.SetJsonResponse(HttpMethod.Get, "api/servers", TwoServers());
        _handler.SetAsyncJsonResponse(HttpMethod.Delete, "api/servers/1", async ct =>
        {
            await releaseDelete.Task.WaitAsync(ct);
            return new { };
        });
        var cut = Render<ServersPage>();
        cut.WaitForAssertion(() => Assert.Contains("web-01", cut.Markup));

        var deletion = cut.InvokeAsync(() => cut.Instance.DeleteServerConfirmedAsync(TwoServers().Items[0]));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("web-01", cut.Markup);
            Assert.Equal(1, cut.FindComponent<AetheusDataGrid<ServerDto>>().Instance.Count);
        });
        Assert.False(deletion.IsCompleted);

        releaseDelete.SetResult();
        await deletion;
        Assert.Contains(_handler.Requests, request => request.Method == HttpMethod.Delete.Method);
    }

    [Fact]
    public async Task OnDeleteServer_InFlightReloadDoesNotRestorePendingRowOrCount()
    {
        var releaseDelete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.SetJsonResponse(HttpMethod.Get, "api/servers", TwoServers());
        _handler.SetAsyncJsonResponse(HttpMethod.Delete, "api/servers/1", async ct =>
        {
            await releaseDelete.Task.WaitAsync(ct);
            return new { };
        });
        var cut = Render<ServersPage>();
        cut.WaitForAssertion(() => Assert.Contains("web-01", cut.Markup));

        var deletion = cut.InvokeAsync(() => cut.Instance.DeleteServerConfirmedAsync(TwoServers().Items[0]));
        var reload = typeof(ServersPage).GetMethod("ReloadGridAsync", InstPriv)!;
        await cut.InvokeAsync(async () => await (Task)reload.Invoke(cut.Instance, [])!);

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("web-01", cut.Markup);
            Assert.Equal(1, cut.FindComponent<AetheusDataGrid<ServerDto>>().Instance.Count);
        });

        releaseDelete.SetResult();
        await deletion;
    }

    [Fact]
    public async Task OnDeleteServer_FailedDeleteRestoresRow()
    {
        _handler.SetJsonResponse(HttpMethod.Get, "api/servers", TwoServers());
        _handler.SetResponse(HttpMethod.Delete, "api/servers/1", System.Net.HttpStatusCode.BadRequest);
        var cut = Render<ServersPage>();
        cut.WaitForAssertion(() => Assert.Contains("web-01", cut.Markup));
        _handler.SetResponse(HttpMethod.Get, "api/servers", System.Net.HttpStatusCode.ServiceUnavailable);

        await cut.InvokeAsync(() => cut.Instance.DeleteServerConfirmedAsync(TwoServers().Items[0]));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("web-01", cut.Markup);
            Assert.Equal(2, cut.FindComponent<AetheusDataGrid<ServerDto>>().Instance.Count);
        });
        var notification = Assert.Single(Services.GetRequiredService<Radzen.NotificationService>().Messages);
        Assert.Equal(Radzen.NotificationSeverity.Error, notification.Severity);
    }

    // ── Render coverage ───────────────────────────────────────────────────────

    [Fact]
    public void Renders_WithTwoServers()
    {
        _handler.SetJsonResponse("api/servers", TwoServers());
        var cut = Render<ServersPage>();

        // The page header and the primary action button render for an authenticated user.
        Assert.Contains("Servers", cut.Markup);
        Assert.Contains("AddAgent", cut.Markup);
    }

    // ── _canWrite reflects permission ─────────────────────────────────────────

    [Fact]
    public void CanWrite_IsTrue_WhenAuthenticated()
    {
        _handler.SetJsonResponse("api/servers", TwoServers());
        var cut = Render<ServersPage>();

        // RegisterServices grants Write on every ResourceType for authenticated users.
        Assert.True(cut.Instance._canWrite);
    }
}
