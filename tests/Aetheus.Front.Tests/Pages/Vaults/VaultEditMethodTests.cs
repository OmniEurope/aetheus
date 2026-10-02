// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Vaults;
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class VaultEditMethodTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public VaultEditMethodTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void GetExpiryBadge_Expired_ReturnsDanger()
    {
        var method = typeof(VaultEdit).GetMethod("GetExpiryBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (OmniTone)method.Invoke(null, [DateTime.UtcNow.AddDays(-1)])!;
        Assert.Equal(OmniTone.Danger, result);
    }

    [Fact]
    public void GetExpiryBadge_ExpiringSoon_ReturnsWarning()
    {
        var method = typeof(VaultEdit).GetMethod("GetExpiryBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (OmniTone)method.Invoke(null, [DateTime.UtcNow.AddDays(7)])!;
        Assert.Equal(OmniTone.Warning, result);
    }

    [Fact]
    public void GetExpiryBadge_FarFuture_ReturnsLight()
    {
        var method = typeof(VaultEdit).GetMethod("GetExpiryBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (OmniTone)method.Invoke(null, [DateTime.UtcNow.AddDays(30)])!;
        Assert.Equal(OmniTone.Neutral, result);
    }

    [Fact]
    public void GetExpiryBadge_ExactlyToday_ReturnsDanger()
    {
        var method = typeof(VaultEdit).GetMethod("GetExpiryBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (OmniTone)method.Invoke(null, [DateTime.UtcNow])!;
        Assert.Equal(OmniTone.Danger, result);
    }

    [Fact]
    public void GetExpiryBadge_Boundary14Days_ReturnsWarning()
    {
        var method = typeof(VaultEdit).GetMethod("GetExpiryBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (OmniTone)method.Invoke(null, [DateTime.UtcNow.AddDays(14)])!;
        Assert.Equal(OmniTone.Warning, result);
    }

    private IRenderedComponent<VaultEdit> RenderNew()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "App" }],
            TotalCount = 1
        });
        return Render<VaultEdit>();
    }

    private IRenderedComponent<VaultEdit> RenderExisting()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "App" }],
            TotalCount = 1
        });
        _handler.SetJsonResponse("api/vaults/3", new VaultDetailDto
        {
            Id = 3,
            Name = "ProdVault",
            Description = "Production secrets",
            Secrets = [new VaultSecretDto { Id = 1, Key = "DB_PASS" }]
        });
        return Render<VaultEdit>(p => p.Add(x => x.Id, 3));
    }

    [Fact]
    public async Task OnSubmit_Create_CallsApi()
    {
        var cut = RenderNew();

        _handler.SetJsonResponse("api/vaults", new VaultDto { Id = 10, Name = "New" });

        var modelField = typeof(VaultEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var model = modelField.GetValue(cut.Instance)!;
        model.GetType().GetProperty("Name")!.SetValue(model, "New Vault");

        var method = typeof(VaultEdit).GetMethod("OnSubmit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var saving = (bool)typeof(VaultEdit)
            .GetField("_saving", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.False(saving);
    }

    [Fact]
    public async Task OnSubmit_Update_CallsApi()
    {
        var cut = RenderExisting();

        _handler.SetJsonResponse("api/vaults/3", new VaultDto { Id = 3, Name = "Updated" });

        var method = typeof(VaultEdit).GetMethod("OnSubmit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var saving = (bool)typeof(VaultEdit)
            .GetField("_saving", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.False(saving);
    }

    [Fact]
    public async Task AddSecret_EmptyKey_MakesNoRequest()
    {
        var cut = RenderExisting();

        SetFormValue(cut.Instance, "_newSecret", "Key", "");
        await cut.InvokeAsync(() => InvokeFormSubmitAsync(cut.Instance, "AddSecret", "_newSecret"));

        // Empty key short-circuits before the create call - no secret POST is sent.
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/vaults/3/secrets"));
    }

    [Fact]
    public async Task AddSecret_WithKey_CallsApi()
    {
        var cut = RenderExisting();

        _handler.SetJsonResponse("api/vaults/3/secrets", new VaultSecretDto { Id = 2, Key = "NEW_KEY" });
        _handler.SetJsonResponse("api/vaults/3", new VaultDetailDto
        {
            Id = 3,
            Name = "ProdVault",
            Secrets = [new VaultSecretDto { Id = 1, Key = "DB_PASS" }, new VaultSecretDto { Id = 2, Key = "NEW_KEY" }]
        });

        SetFormValue(cut.Instance, "_newSecret", "Key", "NEW_KEY");
        SetFormValue(cut.Instance, "_newSecret", "Value", "secret_value");
        await cut.InvokeAsync(() => InvokeFormSubmitAsync(cut.Instance, "AddSecret", "_newSecret"));

        var keyAfter = GetFormValue<string>(cut.Instance, "_newSecret", "Key");
        Assert.Equal(string.Empty, keyAfter);
    }

    [Fact]
    public void Renders_NewVault()
    {
        var cut = RenderNew();
        Assert.Contains("NewVault", cut.Markup);
    }

    [Fact]
    public void Renders_ExistingVault()
    {
        var cut = RenderExisting();
        // Existing vault renders its name and its loaded secret key.
        Assert.Contains("ProdVault", cut.Markup);
        Assert.Contains("DB_PASS", cut.Markup);
    }
}
