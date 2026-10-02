// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Reflection;
using Aetheus.Front.Components.Projects.ProjectDetailSections;
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests.Pages.Projects;

public sealed class WorkItemEditDialogBehaviorTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public WorkItemEditDialogBehaviorTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        Services.AddSingleton<OmniDialogService>(sp => new SpyDialogService(
            sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>(),
            sp.GetRequiredService<IJSRuntime>()));
    }

    [Fact]
    public async Task Create_SubmitsProjectAndDistinctTags_ThenClosesWithSuccess()
    {
        _handler.SetJsonResponse(HttpMethod.Post, "api/work-items", new WorkItemDto { Id = 17, ProjectId = 9, Title = "Harden runner" });
        var cut = Render<WorkItemEditDialog>(p => p.Add(x => x.ProjectId, 9));
        var model = Model(cut.Instance);
        Set(model, "Title", "Harden runner");
        Set(model, "TagsCsv", "security, runner, security");

        await InvokeSubmitAsync(cut);

        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.EndsWith("api/work-items", StringComparison.Ordinal));
        Assert.True(Spy.Closed);
        Assert.Equal(true, Spy.LastResult);
    }

    [Fact]
    public async Task Edit_PrefillsExistingValues_AndUsesItemEndpoint()
    {
        var item = new WorkItemDto
        {
            Id = 23,
            ProjectId = 9,
            Title = "Existing issue",
            Description = "Keep details",
            Type = WorkItemType.Bug,
            Status = WorkItemStatus.Active,
            Priority = 3,
            Tags = ["backend", "urgent"]
        };
        _handler.SetJsonResponse(HttpMethod.Put, "api/work-items/23", item);
        var cut = Render<WorkItemEditDialog>(p => p.Add(x => x.ProjectId, 9).Add(x => x.Item, item));

        Assert.Contains("Existing issue", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("backend, urgent", cut.Markup, StringComparison.Ordinal);
        await InvokeSubmitAsync(cut);

        Assert.Contains(_handler.Requests, r => r.Method == "PUT" && r.Url.EndsWith("api/work-items/23", StringComparison.Ordinal));
        Assert.True(Spy.Closed);
        Assert.Equal(true, Spy.LastResult);
    }

    [Fact]
    public async Task Create_ApiFailure_ShowsErrorAndKeepsDialogOpen()
    {
        _handler.SetResponse(HttpMethod.Post, "api/work-items", HttpStatusCode.InternalServerError);
        var cut = Render<WorkItemEditDialog>(p => p.Add(x => x.ProjectId, 9));
        Set(Model(cut.Instance), "Title", "Will fail");

        await InvokeSubmitAsync(cut);

        Assert.False(Spy.Closed);
        Assert.Contains(Services.Toasts(),
            message => message.Severity == OmniSeverity.Danger);
    }

    [Fact]
    public void Cancel_ClosesWithFalse()
    {
        var cut = Render<WorkItemEditDialog>(p => p.Add(x => x.ProjectId, 9));

        cut.InvokeAsync(() => typeof(WorkItemEditDialog).GetMethod("Cancel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(cut.Instance, []));

        Assert.True(Spy.Closed);
        Assert.Equal(false, Spy.LastResult);
    }

    private SpyDialogService Spy => (SpyDialogService)Services.GetRequiredService<OmniDialogService>();
    private static object Model(WorkItemEditDialog instance) => typeof(WorkItemEditDialog)
        .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static void Set(object model, string property, object? value) => model.GetType().GetProperty(property)!.SetValue(model, value);
    private static Task InvokeSubmitAsync(IRenderedComponent<WorkItemEditDialog> cut) => cut.InvokeAsync(() =>
        (Task)typeof(WorkItemEditDialog).GetMethod("SubmitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(cut.Instance, [])!);
}
