// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class VaultEditExtendedTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly BunitTestHelper.TestHandler _handler;

    public VaultEditExtendedTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private void SetupVault(int id = 1)
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "MyProject" }],
            TotalCount = 1
        });
        _handler.SetJsonResponse($"api/vaults/{id}", new VaultDetailDto
        {
            Id = id,
            Name = "secrets",
            Description = "Production secrets",
            ProjectId = 1,
            ProjectName = "MyProject",
            RowVersion = Guid.NewGuid(),
            Secrets =
            [
                new VaultSecretDto { Id = 1, Key = "DB_PASSWORD", CreatedAt = DateTime.UtcNow }
            ]
        });
    }

    private IRenderedComponent<VaultEdit> RenderExisting(int id = 1)
    {
        SetupVault(id);
        return Render<VaultEdit>(p => p.Add(x => x.Id, id));
    }

    [Fact]
    public void Renders_ExistingVault_ShowsVaultName()
    {
        var cut = RenderExisting();
        Assert.Contains("secrets", cut.Markup);
    }

    [Fact]
    public void Renders_ExistingVault_ShowsSecretKey()
    {
        var cut = RenderExisting();
        Assert.Contains("DB_PASSWORD", cut.Markup);
    }

    [Fact]
    public void Renders_NewVault_ShowsForm()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto> { Items = [], TotalCount = 0 });
        var cut = Render<VaultEdit>();
        Assert.Contains("NewVault", cut.Markup);
    }

    // ImportSecrets removed - calls Dialog.OpenAsync which hangs in bUnit

    // RotateSecret removed - calls Dialog.Confirm which hangs in bUnit

    [Fact]
    public async Task AddSecret_EmptyKey_MakesNoRequest()
    {
        SetupVault();
        var cut = RenderExisting();

        SetFormValue(cut.Instance, "_newSecret", "Key", "");
        await InvokeFormSubmitAsync(cut.Instance, "AddSecret", "_newSecret");

        // Empty key short-circuits before the create call - no secret POST is sent.
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/vaults/1/secrets"));
    }

    [Fact]
    public async Task AddSecret_ValidKey_CallsApi()
    {
        SetupVault();
        _handler.SetJsonResponse("api/vaults/1/secrets", new VaultSecretDto { Id = 2, Key = "NEW_KEY" });
        var cut = RenderExisting();

        SetFormValue(cut.Instance, "_newSecret", "Key", "NEW_KEY");
        SetFormValue(cut.Instance, "_newSecret", "Value", "val");
        await InvokeFormSubmitAsync(cut.Instance, "AddSecret", "_newSecret");

        // A non-empty key drives a real POST to the vault's secrets endpoint.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/vaults/1/secrets"));
    }

    [Fact]
    public async Task ReloadDetail_RefetchesFromApi()
    {
        SetupVault();
        var cut = RenderExisting();

        // Null out _detail first so a re-populated value proves the refetch actually ran (rather than
        // reading the value that was already loaded at render time).
        var detailField = typeof(VaultEdit).GetField("_detail", Priv)!;
        detailField.SetValue(cut.Instance, null);

        var method = typeof(VaultEdit).GetMethod("ReloadDetail", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        var detail = detailField.GetValue(cut.Instance) as VaultDetailDto;
        Assert.NotNull(detail);
        Assert.Equal("secrets", detail!.Name);
    }

    [Fact]
    public async Task ExportKeys_CallsJsAndApi()
    {
        SetupVault();
        _handler.SetJsonResponse("api/vaults/1/export-keys", new List<string> { "DB_PASSWORD" });
        var cut = RenderExisting();

        var method = typeof(VaultEdit).GetMethod("ExportKeys", Priv)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // ExportKeys fetches the key list, then hands it to the JS downloadFile helper.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("export-keys"));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "downloadFile");
    }
}
