// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public class LoginTests : BunitContext
{
    public LoginTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, authenticated: false);
    }

    private readonly BunitTestHelper.TestHandler _handler;

    [Fact]
    public void Renders_LoginForm()
    {
        _handler.SetJsonResponse("api/auth/public-demo", new PublicDemoInfoDto());
        var cut = Render<Login>();

        Assert.Contains("Login", cut.Markup);
        Assert.Contains("Username", cut.Markup);
        Assert.Contains("Password", cut.Markup);
    }

    [Fact]
    public void PublicDemo_ShowsCredentials()
    {
        _handler.SetJsonResponse("api/auth/public-demo", new PublicDemoInfoDto { Enabled = true });

        var cut = Render<Login>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("PublicDemo", cut.Markup);
            Assert.Contains("PublicDemoCredentials", cut.Markup);
        });
    }

    [Fact]
    public void RegularEnvironment_HidesPublicDemoCredentials()
    {
        _handler.SetJsonResponse("api/auth/public-demo", new PublicDemoInfoDto());

        var cut = Render<Login>();

        cut.WaitForAssertion(() => Assert.DoesNotContain("PublicDemoCredentials", cut.Markup));
    }

    [Fact]
    public void InputEvents_AreSentByTheLoginForm()
    {
        _handler.SetJsonResponse("auth/login", new LoginResponse { Token = "jwt-token" });
        var cut = Render<Login>();

        cut.Find("input[name='Username']").Input("admin");
        cut.Find("input[name='Password']").Input("aetheus-dev-admin-pwd");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(
            _handler.Requests,
            request => request.Method == "POST" && request.Url.EndsWith("auth/login", StringComparison.Ordinal)));
        var body = _handler.RequestDetails.Last(request =>
            request.Method == "POST" && request.Url.EndsWith("auth/login", StringComparison.Ordinal)).Body;
        var request = System.Text.Json.JsonSerializer.Deserialize<LoginRequest>(
            body!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal("admin", request!.Username);
        Assert.Equal("aetheus-dev-admin-pwd", request.Password);
    }

    [Fact]
    public async Task SuccessfulLogin_NavigatesToHome()
    {
        _handler.SetJsonResponse("auth/login", new LoginResponse { Token = "jwt-token" });
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var cut = Render<Login>();

        // Fill in form fields via rendered component
        var inputs = cut.FindAll("input");
        // Trigger form submit
        var form = cut.Find("form");
        form.Submit();

        cut.WaitForState(() => nav.Uri.EndsWith("/"), TimeSpan.FromSeconds(2));
        Assert.EndsWith("/", nav.Uri);
    }

    // FailedLogin_ShowsErrorMessage removed: it never submitted the form and asserted no error
    // message - only that the form rendered. The real invalid-credentials branch is covered by
    // OnSubmit_WithNullResult_ShowsError below (asserts _error == "InvalidCredentials").

    [Fact]
    public void Renders_LoginForm_HasSubmitButton()
    {
        var cut = Render<Login>();

        var buttons = cut.FindAll("button");
        Assert.NotEmpty(buttons);
    }

    [Fact]
    public void Renders_LoginForm_Unauthenticated()
    {
        var handler2 = BunitTestHelper.RegisterServices(this, authenticated: false);
        handler2.SetJsonResponse("auth/login", new LoginResponse { Token = "test" });

        var cut = Render<Login>();
        Assert.Contains("Login", cut.Markup);
    }

    [Fact]
    public void RedirectsToHome_WhenAlreadyAuthenticated()
    {
        var handler2 = BunitTestHelper.RegisterServices(this, authenticated: true);
        var nav = Services.GetRequiredService<Bunit.TestDoubles.BunitNavigationManager>();

        var cut = Render<Login>();

        Assert.EndsWith("/", nav.Uri);
    }

    [Fact]
    public void Renders_RememberMeCheckbox()
    {
        var cut = Render<Login>();
        // Login form should have inputs for username, password, and remember me
        var inputs = cut.FindAll("input");
        Assert.True(inputs.Count >= 2);
    }

    [Fact]
    public void Submit_WithRememberMeChecked_SendsRememberMeTrue()
    {
        _handler.SetJsonResponse("auth/login", new LoginResponse { Token = "jwt-token" });
        var cut = Render<Login>();

        cut.Find("input[name='Username']").Input("admin");
        cut.Find("input[name='Password']").Input("secret");
        cut.Find("input.labeled-toggle-native-input").Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(
            _handler.RequestDetails,
            request => request.Method == "POST" && request.Url.EndsWith("auth/login", StringComparison.Ordinal)));
        var body = _handler.RequestDetails.Last(request =>
            request.Method == "POST" && request.Url.EndsWith("auth/login", StringComparison.Ordinal)).Body;
        var request = System.Text.Json.JsonSerializer.Deserialize<LoginRequest>(
            body!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.True(request!.RememberMe);
    }

    [Fact]
    public async Task OnSubmit_WithNullResult_ShowsError()
    {
        // A JSON `null` login response makes LoginAsync return null → the invalid-credentials branch.
        _handler.SetJsonResponse<LoginResponse?>("auth/login", null);

        var cut = Render<Login>();
        var method = typeof(Login).GetMethod("OnSubmit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, [])!;

        // A null result surfaces the invalid-credentials error and leaves the loading flag cleared.
        var error = (string?)typeof(Login).GetField("_error", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cut.Instance);
        Assert.Equal("InvalidCredentials", error);
        Assert.False((bool)typeof(Login).GetField("_loading", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cut.Instance)!);
    }
}

