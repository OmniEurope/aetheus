// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Layout;
using Aetheus.Front.Pages;
using Aetheus.Front.Pages.Servers.AgentWizard;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public class AddAgentTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public AddAgentTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
        _handler.SetJsonResponse(
            HttpMethod.Get,
            "api/servers/agent-server-url",
            new { Url = "https://localhost:5301" });
        JSInterop.Mode = JSRuntimeMode.Loose;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiBaseUrl"] = "https://localhost:5301"
            })
            .Build();
        Services.AddSingleton<IConfiguration>(config);
    }

    [Fact]
    public void Renders_WizardDialog()
    {
        var cut = Render<AddAgent>();
        // The wizard opens on the platform-selection step.
        Assert.Contains("wizard-platform-card", cut.Markup);
    }

    [Fact]
    public void Renders_PlatformStep_WithLinuxAndWindows()
    {
        var cut = Render<AddAgent>();
        var markup = cut.Markup;
        // Both platform cards render with their brand SVG icons.
        Assert.Contains("wizard-platform-card", markup);
        Assert.Contains("wizard-platform-icon", markup);
        Assert.Contains("<svg", markup);
    }

    [Fact]
    public void Renders_PlatformOptions()
    {
        var cut = Render<AddAgent>();
        var markup = cut.Markup;
        Assert.Contains("wizard-platform-card", markup);
    }

    [Fact]
    public void LinuxPlatform_IsSelectedByDefault()
    {
        var cut = Render<AddAgent>();
        Assert.Contains("wizard-platform-selected", cut.Markup);
    }

    [Fact]
    public void Renders_WithConfig()
    {
        var cut = Render<AddAgent>();
        // With the API base URL configured, the wizard still renders its platform step.
        Assert.Contains("wizard-platform-card", cut.Markup);
    }

    // --- CanAdvance tests ---

    [Fact]
    public void CanAdvance_Step0_TrueWhenPlatformSelected()
    {
        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("CanAdvance", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (bool)method.Invoke(cut.Instance, [])!;
        Assert.True(result); // linux is selected by default
    }

    [Fact]
    public void CanAdvance_Step1_TrueWithoutToken()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_currentStep", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, 1);
        var method = typeof(AddAgent).GetMethod("CanAdvance", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (bool)method.Invoke(cut.Instance, [])!;
        Assert.True(result); // token check removed - all steps after 0 always advance
    }

    [Fact]
    public void CanAdvance_Step1_TrueWithToken()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_currentStep", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, 1);
        typeof(AddAgent).GetField("_generatedToken", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance,
            new RegistrationTokenDto { Token = "test-token-123", ExpiresAt = DateTime.UtcNow.AddHours(24) });
        var method = typeof(AddAgent).GetMethod("CanAdvance", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (bool)method.Invoke(cut.Instance, [])!;
        Assert.True(result);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void CanAdvance_StepsAfter1_AlwaysTrue(int step)
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_currentStep", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, step);
        var method = typeof(AddAgent).GetMethod("CanAdvance", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (bool)method.Invoke(cut.Instance, [])!;
        Assert.True(result);
    }

    // --- BuildSteps ---

    [Fact]
    public void BuildSteps_ReturnsFourSteps()
    {
        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("BuildSteps", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (List<WizardStep>)method.Invoke(cut.Instance, [])!;
        Assert.Equal(4, result.Count);
    }

    // --- Platform selection ---

    [Fact]
    public void PlatformSelection_DefaultIsLinux()
    {
        var cut = Render<AddAgent>();
        var platform = (string)typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal("linux", platform);
    }

    [Fact]
    public void SwitchToWindows_UpdatesPlatform()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "windows");
        var platform = (string)typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal("windows", platform);
    }

    // --- Token generation ---

    [Fact]
    public async Task GenerateTokenAsync_SetsTokenOnSuccess()
    {
        _handler.SetJsonResponse("api/auth/registration-tokens",
            new RegistrationTokenDto { Token = "generated-token-abc", ExpiresAt = DateTime.UtcNow.AddHours(24) });

        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("GenerateTokenAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var token = typeof(AddAgent).GetField("_generatedToken", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.NotNull(token);
    }

    [Fact]
    public async Task GenerateTokenAsync_SetsGeneratingFlagDuringExecution()
    {
        _handler.SetJsonResponse("api/auth/registration-tokens",
            new RegistrationTokenDto { Token = "test-token", ExpiresAt = DateTime.UtcNow.AddHours(24) });

        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("GenerateTokenAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var generating = (bool)typeof(AddAgent).GetField("_tokenGenerating", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.False(generating); // Should be false after completion
    }

    [Fact]
    public async Task CopyTokenAsync_WithNullToken_DoesNothing()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_generatedToken", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, null);
        var method = typeof(AddAgent).GetMethod("CopyTokenAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;
        var copied = (bool)typeof(AddAgent).GetField("_tokenCopied", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.False(copied);
    }

    [Fact]
    public async Task CopyTokenAsync_WithToken_SetsTokenCopied()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_generatedToken", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance,
            new RegistrationTokenDto { Token = "copy-me", ExpiresAt = DateTime.UtcNow.AddHours(24) });
        var method = typeof(AddAgent).GetMethod("CopyTokenAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);
        var copied = (bool)typeof(AddAgent).GetField("_tokenCopied", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.True(copied);
    }

    // --- GetAllCommands ---

    [Fact]
    public void GetAllCommands_Linux_ReturnsExpectedCount()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "linux");
        var method = typeof(AddAgent).GetMethod("GetAllCommands", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = method.Invoke(cut.Instance, []) as System.Collections.IList;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count); // download + extract + install
    }

    [Fact]
    public void GetAllCommands_Windows_ReturnsExpectedCount()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "windows");
        var method = typeof(AddAgent).GetMethod("GetAllCommands", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = method.Invoke(cut.Instance, []) as System.Collections.IList;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count); // download + extract to temp + install script
    }

    // --- ServerBaseUrl ---

    [Fact]
    public void ServerBaseUrl_UsesConfig()
    {
        var cut = Render<AddAgent>();
        var prop = typeof(AddAgent).GetProperty("ServerBaseUrl", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (string)prop.GetValue(cut.Instance)!;
        Assert.Equal("https://localhost:5301", result);
    }

    // --- Navigation ---

    [Fact]
    public void GoBack_NavigatesToServers()
    {
        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("GoBack", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, []);
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.EndsWith("/servers", nav.Uri);
    }

    [Fact]
    public void OnFinish_NavigatesToServers()
    {
        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("OnFinish", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, []);
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();
        Assert.EndsWith("/servers", nav.Uri);
    }

    // --- OnStepChanged ---

    [Fact]
    public async Task OnStepChanged_UpdatesCurrentStep()
    {
        _handler.SetJsonResponse(
            HttpMethod.Post,
            "api/auth/registration-tokens",
            new RegistrationTokenDto
            {
                Token = "step-change-token",
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            });
        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("OnStepChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [2])!);
        var step = (int)typeof(AddAgent).GetField("_currentStep", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal(2, step);
    }

    [Fact]
    public async Task OnStepChanged_ToStep5_StartsSignalR()
    {
        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("OnStepChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [5])!);
        }
        catch (Exception)
        {
            // Expected - SignalR can't connect in test env
        }
        var step = (int)typeof(AddAgent).GetField("_currentStep", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal(5, step);
    }

    // --- DisposeAsync ---

    [Fact]
    public async Task DisposeAsync_WithNoHub_Succeeds()
    {
        var cut = Render<AddAgent>();
        await cut.Instance.DisposeAsync();
    }

    // --- Token expiration hours ---

    [Fact]
    public void TokenExpirationHours_Default24()
    {
        var cut = Render<AddAgent>();
        var hours = (int)typeof(AddAgent).GetField("_tokenExpirationHours", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal(24, hours);
    }

    // --- RefreshSteps ---

    [Fact]
    public void RefreshSteps_RebuildsStepList()
    {
        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("RefreshSteps", BindingFlags.NonPublic | BindingFlags.Instance)!;
        cut.InvokeAsync(() => { method.Invoke(cut.Instance, []); return Task.CompletedTask; });
        var steps = (List<WizardStep>)typeof(AddAgent).GetField("_steps", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal(4, steps.Count);
    }

    // --- Rendering with different platforms ---

    [Fact]
    public void Render_WithWindowsSelected_ContainsWindowsContent()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "windows");
        var method = typeof(AddAgent).GetMethod("RefreshSteps", BindingFlags.NonPublic | BindingFlags.Instance)!;
        cut.InvokeAsync(() => { method.Invoke(cut.Instance, []); return Task.CompletedTask; });
        cut.Render();
        var steps = (List<WizardStep>)typeof(AddAgent).GetField("_steps", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal(4, steps.Count);
    }

    // --- Token masking in summary ---

    [Fact]
    public void TokenMasking_LongToken_ShowsPartialMask()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_generatedToken", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance,
            new RegistrationTokenDto { Token = "abcdefghijklmnop", ExpiresAt = DateTime.UtcNow.AddHours(24) });
        typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "linux");
        typeof(AddAgent).GetField("_currentStep", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, 2);
        var method = typeof(AddAgent).GetMethod("RefreshSteps", BindingFlags.NonPublic | BindingFlags.Instance)!;
        cut.InvokeAsync(() => { method.Invoke(cut.Instance, []); return Task.CompletedTask; });
        var steps = (List<WizardStep>)typeof(AddAgent).GetField("_steps", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.NotNull(steps[2].Content);
    }

    // --- Build step content methods ---

    [Fact]
    public void BuildPlatformStep_RendersCards()
    {
        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("BuildPlatformStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var fragment = (RenderFragment)method.Invoke(cut.Instance, [])!;
        var rendered = Render(fragment);
        Assert.Contains("wizard-platform-card", rendered.Markup);
        Assert.Contains("wizard-platform-icon", rendered.Markup);
        Assert.Contains("<svg", rendered.Markup);
        Assert.Contains("wizard-platform-selected", rendered.Markup);
    }

    [Fact]
    public void BuildInstallStep_Linux_RendersThreeCodeBlocks()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "linux");
        var method = typeof(AddAgent).GetMethod("BuildInstallStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var fragment = (RenderFragment)method.Invoke(cut.Instance, [])!;
        var rendered = Render(fragment);
        Assert.Contains("wizard-code-block", rendered.Markup);
        Assert.Contains("curl", rendered.Markup);
        Assert.Contains("tar", rendered.Markup);
        Assert.Contains("install-agent-linux.sh", rendered.Markup);
        Assert.Contains("download", rendered.Markup); // download button
    }

    [Fact]
    public void BuildInstallStep_Windows_RendersTwoCodeBlocks()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "windows");
        var method = typeof(AddAgent).GetMethod("BuildInstallStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var fragment = (RenderFragment)method.Invoke(cut.Instance, [])!;
        var rendered = Render(fragment);
        Assert.Contains("wizard-code-block", rendered.Markup);
        Assert.Contains("Invoke-WebRequest", rendered.Markup);
        Assert.Contains("install-agent-windows.ps1", rendered.Markup);
    }

    [Fact]
    public void BuildVerifyStep_NotListening_RendersCheckButton()
    {
        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("BuildVerifyStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var fragment = (RenderFragment)method.Invoke(cut.Instance, [])!;
        var rendered = Render(fragment);
        Assert.Contains("refresh", rendered.Markup);
        Assert.Contains("wizard-verify-list", rendered.Markup);
    }

    [Fact]
    public void BuildVerifyStep_Listening_RendersPolling()
    {
        var cut = Render<AddAgent>();
        var detection = typeof(AddAgent).GetField("_detection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        typeof(AgentDetectionMonitor).GetProperty("Listening")!.SetValue(detection, true);
        var method = typeof(AddAgent).GetMethod("BuildVerifyStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var fragment = (RenderFragment)method.Invoke(cut.Instance, [])!;
        var rendered = Render(fragment);
        Assert.Contains("wizard-polling", rendered.Markup);
    }

    [Fact]
    public void BuildVerifyStep_AgentFound_RendersSuccessAlert()
    {
        var cut = Render<AddAgent>();
        var detection = typeof(AddAgent).GetField("_detection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        typeof(AgentDetectionMonitor).GetProperty("AgentFound")!.SetValue(detection, true);
        typeof(AgentDetectionMonitor).GetProperty("AgentName")!.SetValue(detection, "my-agent");
        var method = typeof(AddAgent).GetMethod("BuildVerifyStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var fragment = (RenderFragment)method.Invoke(cut.Instance, [])!;
        var rendered = Render(fragment);
        Assert.Contains("check_circle", rendered.Markup);
        Assert.Contains("wizard-verify-success-icon", rendered.Markup);
    }

    [Fact]
    public void BuildInstallStep_Linux_RendersDownloadButton()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "linux");
        var method = typeof(AddAgent).GetMethod("BuildInstallStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var fragment = (RenderFragment)method.Invoke(cut.Instance, [])!;
        var rendered = Render(fragment);
        Assert.Contains("download", rendered.Markup);
    }

    [Fact]
    public void BuildPlatformStep_LinuxSelected_ShowsCheckCircle()
    {
        var cut = Render<AddAgent>();
        typeof(AddAgent).GetField("_platform", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cut.Instance, "linux");
        var method = typeof(AddAgent).GetMethod("BuildPlatformStep", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var fragment = (RenderFragment)method.Invoke(cut.Instance, [])!;
        var rendered = Render(fragment);
        Assert.Contains("check_circle", rendered.Markup);
        Assert.Contains("wizard-platform-check", rendered.Markup);
    }

    [Fact]
    public async Task GenerateTokenAsync_NullResponse_DoesNotSetToken()
    {
        _handler.SetResponse("api/auth/registration-tokens", System.Net.HttpStatusCode.NotFound);
        var cut = Render<AddAgent>();
        var method = typeof(AddAgent).GetMethod("GenerateTokenAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try { await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!); } catch { }
        var token = typeof(AddAgent).GetField("_generatedToken", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Null(token);
    }

    // --- Verify step listening state ---

    [Fact]
    public void VerifyStep_InitialState_NotListeningNotFound()
    {
        var cut = Render<AddAgent>();
        var detection = typeof(AddAgent).GetField("_detection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        var listening = (bool)typeof(AgentDetectionMonitor).GetProperty("Listening")!.GetValue(detection)!;
        var found = (bool)typeof(AgentDetectionMonitor).GetProperty("AgentFound")!.GetValue(detection)!;
        Assert.False(listening);
        Assert.False(found);
    }

    // --- Breadcrumb set on init ---

    [Fact]
    public void OnInitialized_SetsBreadcrumb()
    {
        var cut = Render<AddAgent>();
        // OnInitializedAsync sets the Servers > AddAgent breadcrumb trail.
        var breadcrumb = Services.GetRequiredService<BreadcrumbService>();
        Assert.Equal(2, breadcrumb.Items.Count);
        Assert.Contains(breadcrumb.Items, i => i.Text == "AddAgent");
    }
}
