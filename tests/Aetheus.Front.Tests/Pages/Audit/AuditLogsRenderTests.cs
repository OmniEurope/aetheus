// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Audit;
using Aetheus.Shared.DTOs;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Audit;

public class AuditLogsRenderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type PageType = typeof(AuditLogs);

    public AuditLogsRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);
        _handler.SetJsonResponse("api/audit/actions", new List<string> { "Created", "Updated", "Deleted" });
        _handler.SetJsonResponse("api/audit/entity-types", new List<string> { "Server", "Pipeline", "Project" });
        _handler.SetJsonResponse("api/audit", new PaginatedResult<AuditLogDto>
        {
            Items = [
                new AuditLogDto { Id = 1, Username = "admin", Action = "Created", EntityType = "Server", EntityId = 1, Timestamp = DateTime.UtcNow },
                new AuditLogDto { Id = 2, Username = "user1", Action = "Updated", EntityType = "Pipeline", EntityId = 2, Timestamp = DateTime.UtcNow.AddMinutes(-5) },
                new AuditLogDto { Id = 3, Username = "user2", Action = "Deleted", EntityType = "Project", EntityId = 3, Timestamp = DateTime.UtcNow.AddMinutes(-10) },
                new AuditLogDto { Id = 4, Username = "user3", Action = "Login", EntityType = "User", EntityId = null, Timestamp = DateTime.UtcNow.AddMinutes(-15) }
            ],
            TotalCount = 4
        });
    }

    [Fact]
    public void Renders_WithAdminAuth()
    {
        var cut = Render<AuditLogs>();
        // Admin init loads the filter option lists and the first page of audit rows.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/audit/actions"));
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/audit/entity-types"));
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/audit?page="));
    }

    [Fact]
    public void NonAdmin_Redirects_WithoutCrash()
    {
        using var ctx = new BunitContext();
        BunitTestHelper.RegisterServices(ctx, isAdmin: false);
        var nav = ctx.Services.GetRequiredService<BunitNavigationManager>();
        var cut = ctx.Render<AuditLogs>();

        // The non-admin guard redirects to the home route on init; the logs list stays empty.
        Assert.Contains(nav.History, h => h.Uri == "/");
        var logs = (List<AuditLogDto>)PageType.GetField("_logs", Priv)!.GetValue(cut.Instance)!;
        Assert.Empty(logs);
    }

    [Fact]
    public async Task LoadData_SetsLogs()
    {
        var cut = Render<AuditLogs>();
        await cut.InvokeAsync(() => Task.CompletedTask);
        var method = PageType.GetMethod("LoadData", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [new LoadDataArgs()])!);
        var logs = (List<AuditLogDto>)PageType.GetField("_logs", Priv)!.GetValue(cut.Instance)!;
        Assert.NotEmpty(logs);
    }

    [Fact]
    public async Task ClearFilters_ResetsAllFilters()
    {
        var cut = Render<AuditLogs>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        PageType.GetField("_search", Priv)!.SetValue(cut.Instance, "admin");
        PageType.GetField("_actionFilter", Priv)!.SetValue(cut.Instance, "Created");
        PageType.GetField("_entityFilter", Priv)!.SetValue(cut.Instance, "Server");
        PageType.GetField("_dateFrom", Priv)!.SetValue(cut.Instance, DateTime.Now.AddDays(-7));
        PageType.GetField("_dateTo", Priv)!.SetValue(cut.Instance, DateTime.Now);

        var method = PageType.GetMethod("ClearFilters", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);

        Assert.Null((string?)PageType.GetField("_search", Priv)!.GetValue(cut.Instance));
        Assert.Equal(string.Empty, (string)PageType.GetField("_actionFilter", Priv)!.GetValue(cut.Instance)!);
        Assert.Equal(string.Empty, (string)PageType.GetField("_entityFilter", Priv)!.GetValue(cut.Instance)!);
        Assert.Null((DateTime?)PageType.GetField("_dateFrom", Priv)!.GetValue(cut.Instance));
        Assert.Null((DateTime?)PageType.GetField("_dateTo", Priv)!.GetValue(cut.Instance));
    }

    [Theory]
    [InlineData("Created", BadgeStyle.Success)]
    [InlineData("Updated", BadgeStyle.Info)]
    [InlineData("Deleted", BadgeStyle.Danger)]
    [InlineData("Login", BadgeStyle.Light)]
    public void GetActionBadge_ReturnsExpectedStyle(string action, BadgeStyle expected)
    {
        var method = PageType.GetMethod("GetActionBadge", PrivStatic)!;
        var result = (BadgeStyle)method.Invoke(null, [action])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void OnSearchChanged_StartsDebouncerWithoutCrash()
    {
        var cut = Render<AuditLogs>();
        var method = PageType.GetMethod("OnSearchChanged", Priv)!;
        method.Invoke(cut.Instance, ["test"]);
        // OnSearchChanged arms the debounce timer (replacing any prior one).
        var timer = (Timer?)PageType.GetField("_debounceTimer", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(timer);
    }

    [Fact]
    public void Dispose_DisposesDebounceTimer()
    {
        var cut = Render<AuditLogs>();
        // Arm a debounce timer, then dispose - Dispose must tear it down without throwing,
        // and a second Dispose stays a safe no-op.
        PageType.GetMethod("OnSearchChanged", Priv)!.Invoke(cut.Instance, ["test"]);
        Assert.NotNull((Timer?)PageType.GetField("_debounceTimer", Priv)!.GetValue(cut.Instance));

        cut.Instance.Dispose();
        var ex = Record.Exception(() => cut.Instance.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public async Task LoadData_WithPaginationArgs_SetsTotalCount()
    {
        var cut = Render<AuditLogs>();
        await cut.InvokeAsync(() => Task.CompletedTask);
        var method = PageType.GetMethod("LoadData", Priv)!;
        var args = new LoadDataArgs { Skip = 0, Top = 10 };
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [args])!);
        // _count mirrors the stub's TotalCount (4) for the grid's server-side pager.
        var count = (int)PageType.GetField("_count", Priv)!.GetValue(cut.Instance)!;
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task ReloadData_SetsLoadingFalseAfterCompletion()
    {
        var cut = Render<AuditLogs>();
        await cut.InvokeAsync(() => Task.CompletedTask);
        var method = PageType.GetMethod("ReloadData", Priv)!;
        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, [])!);
        var loading = (bool)PageType.GetField("_loading", Priv)!.GetValue(cut.Instance)!;
        Assert.False(loading);
    }
}
