// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Reflection;
using Aetheus.Front.Components.Alerts;
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using OmniEurope.Blazor.Components;

namespace Aetheus.Front.Tests.Pages.AlertsDeep;

// X4D8: the alert create/edit form lives in AlertEditDialog now (extracted from the Alerts list page).
public class AlertEditDialogTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public AlertEditDialogTests() => _handler = BunitTestHelper.RegisterServices(this);

    /// <summary>
    /// Registers a spy OmniDialogService (lazy factory so the provider isn't built early) that records the
    /// Close payload. SubmitAsync routes its success close through the injected OmniDialogService, so we can
    /// assert the dialog closed with <c>true</c> (signalling the caller to reload the list).
    /// </summary>
    private void RegisterSpyDialog() =>
        Services.AddSingleton<OmniDialogService>(sp => new SpyDialogService(
            sp.GetRequiredService<NavigationManager>(),
            sp.GetRequiredService<IJSRuntime>()));

    private SpyDialogService Spy() => (SpyDialogService)Services.GetRequiredService<OmniDialogService>();

    [Fact]
    public void Renders_CreateForm_WhenNoAlert()
    {
        var cut = Render<AlertEditDialog>();

        Assert.Contains("AlertName", cut.Markup);
        Assert.Contains("Metric", cut.Markup);
        Assert.Contains("Threshold", cut.Markup);
        // Create mode: the "Active" (IsEnabled) switch is hidden.
        Assert.DoesNotContain("Active", cut.Markup);
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Create"));
    }

    [Fact]
    public void Renders_PrefilledForm_AndActiveSwitch_WhenAlertProvided()
    {
        var alert = new AlertRuleDto
        {
            Id = 7,
            Name = "CPU High",
            Metric = MetricType.Cpu,
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 88,
            SustainedSeconds = 90,
            Severity = AlertSeverity.Critical,
            IsEnabled = true
        };

        var cut = Render<AlertEditDialog>(p => p.Add(x => x.Alert, alert));

        Assert.Contains("CPU High", cut.Markup);
        Assert.Contains("Active", cut.Markup);
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Save"));
    }

    [Fact]
    public void Renders_CancelButton()
    {
        var cut = Render<AlertEditDialog>();
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("GoBack"));
    }

    [Fact]
    public async Task Submit_EmptyName_TakesWarningPath_WithoutThrowing()
    {
        var cut = Render<AlertEditDialog>(); // create mode, Name empty by default
        var submit = typeof(AlertEditDialog).GetMethod("SubmitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)submit.Invoke(cut.Instance, [])!);
        // Empty name short-circuits on a warning toast - no create POST is sent.
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/alerts"));
    }

    // Success-path coverage (restored after the AlertEditDialog extraction dropped it): a valid create
    // must actually POST to api/alerts; the API returning the created rule drives the success branch
    // (success toast), distinguishing it from the error/warning branches.
    [Fact]
    public async Task Submit_CreateMode_WithName_SendsCreatePost_AndSucceeds()
    {
        _handler.SetJsonResponse(HttpMethod.Post, "api/alerts", new AlertRuleDto { Id = 42, Name = "Disk High" });
        var cut = Render<AlertEditDialog>(); // create mode
        SetModelName(cut.Instance, "Disk High");

        await InvokeSubmit(cut);

        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/alerts"));
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "PUT");
        Assert.Contains(Notifications.Toasts(), m => m.Severity == OmniSeverity.Success);
    }

    [Fact]
    public async Task Submit_EditMode_SendsUpdatePut_AndSucceeds()
    {
        var alert = new AlertRuleDto
        {
            Id = 7,
            Name = "CPU High",
            Metric = MetricType.Cpu,
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 88,
            SustainedSeconds = 90,
            Severity = AlertSeverity.Critical,
            IsEnabled = true
        };
        _handler.SetJsonResponse(HttpMethod.Put, "api/alerts/7", alert);
        var cut = Render<AlertEditDialog>(p => p.Add(x => x.Alert, alert)); // edit mode, name prefilled

        await InvokeSubmit(cut);

        Assert.Contains(_handler.Requests, r => r.Method == "PUT" && r.Url.Contains("api/alerts/7"));
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST");
        Assert.Contains(Notifications.Toasts(), m => m.Severity == OmniSeverity.Success);
    }

    // The API returning no rule (null) must surface as the error branch, not a silent success.
    [Fact]
    public async Task Submit_CreateMode_WhenApiReturnsNull_TakesErrorPath()
    {
        _handler.SetResponse(HttpMethod.Post, "api/alerts", HttpStatusCode.InternalServerError);
        var cut = Render<AlertEditDialog>();
        SetModelName(cut.Instance, "Disk High");

        await InvokeSubmit(cut);

        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/alerts"));
        Assert.Contains(Notifications.Toasts(), m => m.Severity == OmniSeverity.Danger);
        Assert.DoesNotContain(Notifications.Toasts(), m => m.Severity == OmniSeverity.Success);
    }

    private OmniOverlayService Notifications => Services.GetRequiredService<OmniOverlayService>();

    private static void SetModelName(AlertEditDialog instance, string name)
    {
        var modelField = typeof(AlertEditDialog).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var model = (UpdateAlertRuleRequest)modelField.GetValue(instance)!;
        model.Name = name;
    }

    private async Task InvokeSubmit(IRenderedComponent<AlertEditDialog> cut)
    {
        var submit = typeof(AlertEditDialog).GetMethod("SubmitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)submit.Invoke(cut.Instance, [])!);
    }

    [Fact]
    public void MetricTypes_LocalizedInstanceList_NotEmpty()
    {
        var cut = Render<AlertEditDialog>();
        var field = typeof(AlertEditDialog).GetField("_metricTypes", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotEmpty((System.Collections.IList)field.GetValue(cut.Instance)!);
    }

    [Fact]
    public void SeverityTypes_LocalizedInstanceList_NotEmpty()
    {
        var cut = Render<AlertEditDialog>();
        var field = typeof(AlertEditDialog).GetField("_severityTypes", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotEmpty((System.Collections.IList)field.GetValue(cut.Instance)!);
    }

    [Fact]
    public async Task Submit_CreateMode_PostsAlert_AndClosesDialogTrue()
    {
        RegisterSpyDialog();
        var created = new AlertRuleDto { Id = 1, Name = "Disk High", Metric = MetricType.Disk };
        _handler.SetJsonResponse(HttpMethod.Post, "api/alerts", created);

        var cut = Render<AlertEditDialog>(); // create mode
        var model = typeof(AlertEditDialog).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        model.GetType().GetProperty("Name")!.SetValue(model, "Disk High");

        var submit = typeof(AlertEditDialog).GetMethod("SubmitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)submit.Invoke(cut.Instance, [])!);

        // A valid name routes through Ui.RunAsync → a real create POST to api/alerts…
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/alerts"));
        // …and on a non-null result the dialog closes with `true` so the list reloads.
        Assert.True(Spy().Closed);
        Assert.Equal(true, Spy().LastResult);
    }

    [Fact]
    public async Task Submit_EditMode_PutsAlert_AndClosesDialogTrue()
    {
        RegisterSpyDialog();
        var alert = new AlertRuleDto
        {
            Id = 7,
            Name = "CPU High",
            Metric = MetricType.Cpu,
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 88,
            SustainedSeconds = 90,
            Severity = AlertSeverity.Critical,
            IsEnabled = true
        };
        _handler.SetJsonResponse(HttpMethod.Put, "api/alerts/7", alert);

        var cut = Render<AlertEditDialog>(p => p.Add(x => x.Alert, alert)); // edit mode, name prefilled

        var submit = typeof(AlertEditDialog).GetMethod("SubmitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)submit.Invoke(cut.Instance, [])!);

        // Edit mode issues a PUT to the per-id route and closes with `true`.
        Assert.Contains(_handler.Requests, r => r.Method == "PUT" && r.Url.Contains("api/alerts/7"));
        Assert.True(Spy().Closed);
        Assert.Equal(true, Spy().LastResult);
    }
}
