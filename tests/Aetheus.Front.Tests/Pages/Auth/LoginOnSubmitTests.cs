// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Auth;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages;

/// <summary>Covers Login.OnSubmit - the login flow (success, TOTP-required, invalid, network error).</summary>
public class LoginOnSubmitTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public LoginOnSubmitTests()
    {
        // Unauthenticated so OnInitialized doesn't redirect away.
        _handler = BunitTestHelper.RegisterServices(this, authenticated: false);
    }

    private static void SetCredentials(object instance, string user, string pass)
    {
        var model = typeof(Login).GetField("_model", Priv)!.GetValue(instance)!;
        var mt = model.GetType();
        mt.GetProperty("Username")!.SetValue(model, user);
        mt.GetProperty("Password")!.SetValue(model, pass);
    }

    private static Task InvokeSubmit(IRenderedComponent<Login> cut)
    {
        var m = typeof(Login).GetMethod("OnSubmit", Priv)!;
        return cut.InvokeAsync(async () => await (Task)m.Invoke(cut.Instance, [])!);
    }

    [Fact]
    public async Task OnSubmit_ValidCredentials_LogsInAndNavigates()
    {
        _handler.SetJsonResponse("api/auth/login", new LoginResponse { Token = MakeJwt(), RefreshToken = "r" });
        _handler.SetJsonResponse("api/users/me/permissions", new UserPermissionSummaryDto { EffectivePermissions = [] });

        var cut = Render<Login>();
        SetCredentials(cut.Instance, "admin", "password123");
        await InvokeSubmit(cut);

        // _error stays null on success
        var error = (string?)typeof(Login).GetField("_error", Priv)!.GetValue(cut.Instance);
        Assert.Null(error);
    }

    [Fact]
    public async Task OnSubmit_TotpRequired_ShowsTotpInput()
    {
        _handler.SetJsonResponse("api/auth/login", new LoginResponse { Token = null!, TotpRequired = true });

        var cut = Render<Login>();
        SetCredentials(cut.Instance, "admin", "password123");
        await InvokeSubmit(cut);

        var totp = (bool)typeof(Login).GetField("_totpRequired", Priv)!.GetValue(cut.Instance)!;
        Assert.True(totp);
    }

    [Fact]
    public async Task TotpRequired_RecoveryMode_SubmitsRecoveryCodeInsteadOfTotpCode()
    {
        _handler.SetJsonResponse("api/auth/login", new LoginResponse { Token = null!, TotpRequired = true });
        var cut = Render<Login>();
        SetCredentials(cut.Instance, "admin", "password123");
        await InvokeSubmit(cut);

        cut.Render();
        cut.Find(".totp-mode-toggle button").Click();
        cut.Find("input[name='RecoveryCode']").Change("ABCDE-12345");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var body = _handler.RequestDetails.Last(request =>
                request.Method == "POST" && request.Url.EndsWith("api/auth/login", StringComparison.Ordinal)).Body;
            var login = System.Text.Json.JsonSerializer.Deserialize<LoginRequest>(
                body!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            Assert.Equal("ABCDE-12345", login!.RecoveryCode);
            Assert.Null(login.TotpCode);
        });
    }

    [Fact]
    public async Task OnSubmit_NullResult_SetsInvalidCredentialsError()
    {
        _handler.SetResponse("api/auth/login", System.Net.HttpStatusCode.Unauthorized);

        var cut = Render<Login>();
        SetCredentials(cut.Instance, "admin", "wrong");
        await InvokeSubmit(cut);

        var error = (string?)typeof(Login).GetField("_error", Priv)!.GetValue(cut.Instance);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task OnSubmit_RateLimited_ShowsSpecificInlineError()
    {
        _handler.SetResponse("api/auth/login", System.Net.HttpStatusCode.TooManyRequests);
        var cut = Render<Login>();
        SetCredentials(cut.Instance, "admin", "password123");

        await InvokeSubmit(cut);

        var error = (string?)typeof(Login).GetField("_error", Priv)!.GetValue(cut.Instance);
        Assert.Equal("LoginRateLimited", error);
    }

    [Fact]
    public async Task OnSubmit_ClearsLoadingFlagAfterCompletion()
    {
        _handler.SetJsonResponse("api/auth/login", new LoginResponse { Token = MakeJwt() });
        _handler.SetJsonResponse("api/users/me/permissions", new UserPermissionSummaryDto { EffectivePermissions = [] });

        var cut = Render<Login>();
        SetCredentials(cut.Instance, "admin", "password123");
        await InvokeSubmit(cut);

        var loading = (bool)typeof(Login).GetField("_loading", Priv)!.GetValue(cut.Instance)!;
        Assert.False(loading);
    }

    [Fact]
    public void ToggleShowPassword_Flips()
    {
        var cut = Render<Login>();
        var m = typeof(Login).GetMethod("ToggleShowPassword", Priv)!;
        m.Invoke(cut.Instance, []);
        var shown = (bool)typeof(Login).GetField("_showPassword", Priv)!.GetValue(cut.Instance)!;
        Assert.True(shown);
        m.Invoke(cut.Instance, []);
        Assert.False((bool)typeof(Login).GetField("_showPassword", Priv)!.GetValue(cut.Instance)!);
    }

    private static string MakeJwt()
    {
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}"));
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { sub = "admin", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        var p64 = Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{p64}.sig";
    }
}
