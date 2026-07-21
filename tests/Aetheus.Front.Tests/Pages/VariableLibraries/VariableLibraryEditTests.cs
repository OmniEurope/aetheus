// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public class VariableLibraryEditTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public VariableLibraryEditTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    private void SetupEditMocks(int id = 1)
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "Project1" }],
            TotalCount = 1
        });
        _handler.SetJsonResponse($"api/variable-libraries/{id}", new VariableLibraryDetailDto
        {
            Id = id,
            Name = "Shared Variables",
            Description = "Shared across environments",
            ProjectId = 1,
            ProjectName = "Project1",
            RowVersion = Guid.NewGuid(),
            Entries =
            [
                new VariableEntryDto { Id = 1, Key = "BASE_URL", Value = "https://example.com" },
                new VariableEntryDto { Id = 2, Key = "API_KEY", Value = "secret-key-123" }
            ]
        });
        _handler.SetJsonResponse($"api/variable-libraries/{id}/entries", new VariableEntryDto { Id = 3, Key = "NEW", Value = "val" });
        _handler.SetJsonResponse($"api/variable-libraries/{id}/export", new List<VariableEntryDto>());
        _handler.SetJsonResponse("api/variable-libraries", new VariableLibraryDto { Id = id, Name = "Created" });
    }

    [Fact]
    public void Renders_NewVariableLibraryPage()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto> { Items = [], TotalCount = 0 });
        var cut = Render<VariableLibraryEdit>();
        Assert.Contains("NewVariableLibrary", cut.Markup);
    }

    [Fact]
    public void Renders_ExistingVariableLibraryPage()
    {
        SetupEditMocks();
        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 1));
        Assert.Contains("Shared Variables", cut.Markup);
    }

    [Fact]
    public void Renders_NewLibrary_WithProjects()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 1, Name = "Project1" }],
            TotalCount = 1
        });
        var cut = Render<VariableLibraryEdit>();
        Assert.Contains("NewVariableLibrary", cut.Markup);
    }

    [Fact]
    public void Renders_LibraryWithMultipleEntries()
    {
        SetupEditMocks();
        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 1));
        // The loaded library's name and both entry keys render in the entries grid.
        Assert.Contains("Shared Variables", cut.Markup);
        Assert.Contains("BASE_URL", cut.Markup);
        Assert.Contains("API_KEY", cut.Markup);
    }

    // --- Method-level tests ---

    [Fact]
    public async Task OnSubmit_NewLibrary_CreatesAndNavigates()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto> { Items = [], TotalCount = 0 });
        _handler.SetJsonResponse("api/variable-libraries", new VariableLibraryDto { Id = 5, Name = "New Lib" });

        var cut = Render<VariableLibraryEdit>();

        var model = typeof(VariableLibraryEdit).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        model.GetType().GetProperty("Name")!.SetValue(model, "New Lib");

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var method = typeof(VariableLibraryEdit).GetMethod("OnSubmit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // Create POSTs to the collection endpoint and navigates to the new library (Id=5).
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/variable-libraries"));
        Assert.EndsWith("/variable-libraries/5", nav.Uri);
    }

    [Fact]
    public async Task OnSubmit_ExistingLibrary_Updates()
    {
        SetupEditMocks();
        _handler.SetJsonResponse(HttpMethod.Put, "api/variable-libraries/1", new VariableLibraryDto { Id = 1, Name = "Updated" });

        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 1));

        var notif = Services.GetRequiredService<Radzen.NotificationService>();
        var method = typeof(VariableLibraryEdit).GetMethod("OnSubmit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // Edit PUTs to the id endpoint and surfaces a success toast.
        Assert.Contains(_handler.Requests, r => r.Method == "PUT" && r.Url.Contains("api/variable-libraries/1"));
        Assert.Contains(notif.Messages, m => m.Severity == Radzen.NotificationSeverity.Success);
    }

    [Fact]
    public async Task AddEntry_EmptyKey_DoesNothing()
    {
        SetupEditMocks();
        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 1));

        SetFormValue(cut.Instance, "_newEntry", "Key", "");
        await InvokeFormSubmitAsync(cut.Instance, "AddEntry", "_newEntry");

        // Empty key short-circuits before the create call - no entry POST is sent.
        Assert.DoesNotContain(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/variable-libraries/1/entries"));
    }

    [Fact]
    public async Task AddEntry_ValidKey_CallsApi()
    {
        SetupEditMocks();
        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 1));

        SetFormValue(cut.Instance, "_newEntry", "Key", "NEW_VAR");
        SetFormValue(cut.Instance, "_newEntry", "Value", "some-value");
        await InvokeFormSubmitAsync(cut.Instance, "AddEntry", "_newEntry");

        // A valid key drives a real POST to the library's entries endpoint.
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/variable-libraries/1/entries"));
    }

    [Fact]
    public void EditEntry_SetsFields()
    {
        SetupEditMocks();
        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 1));

        var entry = new VariableEntryDto { Id = 1, Key = "BASE_URL", Value = "https://example.com" };
        var method = typeof(VariableLibraryEdit).GetMethod("EditEntry", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(cut.Instance, [entry]);

        var editKey = (string)typeof(VariableLibraryEdit).GetField("_editEntryKey", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance)!;
        Assert.Equal("BASE_URL", editKey);
    }

    [Fact]
    public void Renders_LibraryWithNoProject()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto> { Items = [], TotalCount = 0 });
        _handler.SetJsonResponse("api/variable-libraries/2", new VariableLibraryDetailDto
        {
            Id = 2,
            Name = "Unscoped Vars",
            RowVersion = Guid.NewGuid(),
            Entries = []
        });

        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 2));
        // Library without a project still renders its name in the header.
        Assert.Contains("Unscoped Vars", cut.Markup);
    }

    [Fact]
    public void Renders_LibraryWithManyEntries()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto> { Items = [], TotalCount = 0 });
        _handler.SetJsonResponse("api/variable-libraries/3", new VariableLibraryDetailDto
        {
            Id = 3,
            Name = "Big Lib",
            RowVersion = Guid.NewGuid(),
            Entries = Enumerable.Range(1, 20).Select(i => new VariableEntryDto
            {
                Id = i,
                Key = $"VAR_{i}",
                Value = $"value-{i}"
            }).ToList()
        });

        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 3));
        // A library with many entries renders its name and the entry keys (first and last).
        Assert.Contains("Big Lib", cut.Markup);
        Assert.Contains("VAR_1", cut.Markup);
        Assert.Contains("VAR_20", cut.Markup);
    }

    // S-TECH-R9XM: unique coverage consolidated here from the former VariableLibraryEditRenderTests
    // (the rest of that file, like VariableLibraryEditMethodTests, only duplicated tests above).

    [Fact]
    public void IsNew_IsTrueWhenIdIsNull()
    {
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto> { Items = [], TotalCount = 0 });
        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, (int?)null));

        var isNew = (bool)typeof(VariableLibraryEdit)
            .GetProperty("_isNew", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.True(isNew);
    }

    [Fact]
    public void IsNew_IsFalseWhenIdIsSet()
    {
        SetupEditMocks();
        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 1));

        var isNew = (bool)typeof(VariableLibraryEdit)
            .GetProperty("_isNew", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        Assert.False(isNew);
    }

    [Fact]
    public async Task ExportEntries_InvokesJsDownload()
    {
        SetupEditMocks();
        var cut = Render<VariableLibraryEdit>(p => p.Add(x => x.Id, 1));

        var method = typeof(VariableLibraryEdit).GetMethod("ExportEntries", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(async () => await (Task)method.Invoke(cut.Instance, [])!);

        // ExportEntries fetches the entries, then hands the JSON to the JS downloadFile helper.
        Assert.Contains(_handler.Requests, r => r.Url.Contains("api/variable-libraries/1/export"));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "downloadFile");
    }
}
