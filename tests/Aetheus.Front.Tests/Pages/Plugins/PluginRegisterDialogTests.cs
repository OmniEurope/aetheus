// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Reflection;
using Aetheus.Front.Pages.Plugins;
using Aetheus.Front.Tests.TestDoubles;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Tests.Pages.Plugins;

// X4D8: the plugin-register form lives in PluginRegisterDialog now (extracted from the inline panel on
// PluginManagement). These tests replace the old inline-form register tests on the management page.
public class PluginRegisterDialogTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public PluginRegisterDialogTests() => _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);

    private void RegisterSpyDialog() =>
        Services.AddSingleton<DialogService>(sp => new SpyDialogService(
            sp.GetRequiredService<NavigationManager>(),
            sp.GetRequiredService<IJSRuntime>()));

    private SpyDialogService Spy() => (SpyDialogService)Services.GetRequiredService<DialogService>();
    private NotificationService Notifications => Services.GetRequiredService<NotificationService>();

    private static void SetModel(PluginRegisterDialog instance, string name, string version)
    {
        var model = (RegisterPluginRequest)typeof(PluginRegisterDialog)
            .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(instance)!;
        model.Name = name;
        model.Version = version;
    }

    private static async Task InvokeSubmit(IRenderedComponent<PluginRegisterDialog> cut)
    {
        var submit = typeof(PluginRegisterDialog).GetMethod("SubmitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)submit.Invoke(cut.Instance, [])!);
    }

    [Fact]
    public void Renders_NameVersionFields_AndButtons()
    {
        RegisterSpyDialog();
        var cut = Render<PluginRegisterDialog>();

        Assert.Contains("PluginName", cut.Markup);
        Assert.Contains("Version", cut.Markup);
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Register"));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains("Cancel"));
    }

    [Fact]
    public async Task Submit_WithValidData_SendsPost_AndClosesWithTrue()
    {
        RegisterSpyDialog();
        _handler.SetJsonResponse(HttpMethod.Post, "api/plugins",
            new PluginRegistrationDto { Id = 9, Name = "DockerScanner", Version = "1.0.0" });
        var cut = Render<PluginRegisterDialog>();
        SetModel(cut.Instance, "DockerScanner", "1.0.0");

        await InvokeSubmit(cut);

        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/plugins"));
        Assert.Contains(Notifications.Messages, m => m.Severity == NotificationSeverity.Success);
        Assert.True(Spy().Closed);
        Assert.Equal(true, Spy().LastResult);
    }

    [Fact]
    public async Task Submit_WhenApiReturnsNull_TakesErrorPath_AndDoesNotClose()
    {
        RegisterSpyDialog();
        _handler.SetResponse(HttpMethod.Post, "api/plugins", HttpStatusCode.InternalServerError);
        var cut = Render<PluginRegisterDialog>();
        SetModel(cut.Instance, "DockerScanner", "1.0.0");

        await InvokeSubmit(cut);

        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/plugins"));
        Assert.Contains(Notifications.Messages, m => m.Severity == NotificationSeverity.Error);
        Assert.DoesNotContain(Notifications.Messages, m => m.Severity == NotificationSeverity.Success);
        Assert.False(Spy().Closed);
    }
}
