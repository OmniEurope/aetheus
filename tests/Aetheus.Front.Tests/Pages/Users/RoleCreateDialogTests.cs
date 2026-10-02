// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Users;
using Aetheus.Front.Tests.TestDoubles;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Aetheus.Front.Tests.Pages.Users;

/// <summary>
/// Behavioural tests for RoleCreateDialog.razor.cs: the form renders the name/description fields,
/// and a valid OnSubmit issues the real POST and closes the dialog with the created role
/// (captured by a spy OmniDialogService).
/// </summary>
public class RoleCreateDialogTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly BunitTestHelper.TestHandler _handler;

    public RoleCreateDialogTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, authenticated: true, isAdmin: true);
    }

    private void RegisterSpyDialog() =>
        Services.AddSingleton<OmniDialogService>(sp => new SpyDialogService(
            sp.GetRequiredService<NavigationManager>(),
            sp.GetRequiredService<IJSRuntime>()));

    private SpyDialogService Spy() => (SpyDialogService)Services.GetRequiredService<OmniDialogService>();

    private static void SetModel(IRenderedComponent<RoleCreateDialog> cut, string prop, object? value)
    {
        var model = typeof(RoleCreateDialog).GetField("_model", Priv)!.GetValue(cut.Instance)!;
        model.GetType().GetProperty(prop)!.SetValue(model, value);
    }

    [Fact]
    public void Renders_NameAndDescriptionFields()
    {
        var cut = Render<RoleCreateDialog>();

        // Localized field labels (stub returns key verbatim) appear in the markup.
        Assert.Contains("Name", cut.Markup);
        Assert.Contains("Description", cut.Markup);
        // A text input for the role name is rendered.
        Assert.NotEmpty(cut.FindAll("input"));
    }

    [Fact]
    public async Task OnSubmit_Valid_PostsCreateRole_AndClosesWithDto()
    {
        RegisterSpyDialog();
        _handler.SetJsonResponse(HttpMethod.Post, "api/roles",
            new RoleDto { Id = 3, Name = "Auditor", Description = "Read-only audit role" });

        var cut = Render<RoleCreateDialog>();
        SetModel(cut, "Name", "Auditor");
        SetModel(cut, "Description", "Read-only audit role");

        await cut.InvokeAsync(async () =>
            await (Task)typeof(RoleCreateDialog).GetMethod("OnSubmit", Priv)!.Invoke(cut.Instance, [])!);

        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/roles"));
        Assert.True(Spy().Closed);
        var dto = Assert.IsType<RoleDto>(Spy().LastResult);
        Assert.Equal("Auditor", dto.Name);
    }

    [Fact]
    public async Task OnSubmit_NullResult_SetsErrorAndDoesNotClose()
    {
        RegisterSpyDialog();
        _handler.SetResponse(HttpMethod.Post, "api/roles", System.Net.HttpStatusCode.Conflict);

        var cut = Render<RoleCreateDialog>();
        SetModel(cut, "Name", "Auditor");

        await cut.InvokeAsync(async () =>
            await (Task)typeof(RoleCreateDialog).GetMethod("OnSubmit", Priv)!.Invoke(cut.Instance, [])!);

        Assert.False(Spy().Closed);
        var error = (string?)typeof(RoleCreateDialog).GetField("_error", Priv)!.GetValue(cut.Instance);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
