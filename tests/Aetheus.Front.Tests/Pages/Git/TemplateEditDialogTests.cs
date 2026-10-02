// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Pages;

public class TemplateEditDialogTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public TemplateEditDialogTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_NewTemplate_FormFields()
    {
        var cut = Render<TemplateEditDialog>();

        Assert.Contains("Name", cut.Markup);
        Assert.Contains("Category", cut.Markup);
        Assert.Contains("Description", cut.Markup);
        Assert.Contains("YAML", cut.Markup);
    }

    [Fact]
    public void Renders_NewTemplate_Buttons()
    {
        var cut = Render<TemplateEditDialog>();

        Assert.Contains("Save", cut.Markup);
        Assert.Contains("GoBack", cut.Markup);
    }

    [Fact]
    public void Renders_EditTemplate_WithData()
    {
        var template = new PipelineTemplateDto
        {
            Id = 1,
            Name = "CI Template",
            Description = "Standard CI",
            Category = "Build",
            YamlContent = "trigger: manual\nstages: []",
            Changelog = "v1: Initial"
        };
        var cut = Render<TemplateEditDialog>(p => p.Add(x => x.Template, template));

        Assert.Contains("CI Template", cut.Markup);
        Assert.Contains("ChangelogEntry", cut.Markup);
    }

    [Fact]
    public void Renders_EditTemplate_ShowsChangelog()
    {
        var template = new PipelineTemplateDto
        {
            Id = 1,
            Name = "Deploy",
            Description = "Deploy template",
            Category = "Deploy",
            YamlContent = "trigger: webhook",
            Changelog = "v1: created"
        };
        var cut = Render<TemplateEditDialog>(p => p.Add(x => x.Template, template));

        // The historical changelog value passed in is rendered, not just the label.
        Assert.Contains("v1: created", cut.Markup);
    }

    [Fact]
    public void Renders_NewTemplate_WithInitialChangelog()
    {
        var cut = Render<TemplateEditDialog>();

        Assert.Contains("ChangelogEntry", cut.Markup);
    }

    [Fact]
    public void InputEvents_AreSentByTheTemplateForm()
    {
        _handler.SetJsonResponse("api/pipelines/templates",
            new PipelineTemplateDto { Id = 5, Name = "Release", Category = "Pipeline" });
        var cut = Render<TemplateEditDialog>();
        var inputs = cut.FindAll("input, textarea");

        inputs[0].Input("Release");
        inputs[1].Input("Pipeline");
        inputs[2].Input("Release pipeline");
        inputs[3].Input("name: aetheus-release");
        inputs[4].Input("Synchronize with source");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(
            _handler.Requests,
            request => request.Method == "POST" && request.Url.EndsWith("api/pipelines/templates", StringComparison.Ordinal)));
        var body = _handler.RequestDetails.Last(request =>
            request.Method == "POST" && request.Url.EndsWith("api/pipelines/templates", StringComparison.Ordinal)).Body;
        var request = System.Text.Json.JsonSerializer.Deserialize<CreatePipelineTemplateRequest>(
            body!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal("Release", request!.Name);
        Assert.Equal("Pipeline", request.Category);
        Assert.Equal("Release pipeline", request.Description);
        Assert.Equal("name: aetheus-release", request.YamlContent);
        Assert.Equal("Synchronize with source", request.ChangelogEntry);
    }

    [Fact]
    public async Task OnSubmit_NewTemplate_PostsCreateToTemplatesEndpoint()
    {
        _handler.SetJsonResponse("api/pipelines/templates",
            new PipelineTemplateDto { Id = 5, Name = "New CI", Category = "Build" });

        var cut = Render<TemplateEditDialog>();

        var model = typeof(TemplateEditDialog)
            .GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        var modelType = model.GetType();
        modelType.GetProperty("Name")!.SetValue(model, "New CI");
        modelType.GetProperty("Category")!.SetValue(model, "Build");
        modelType.GetProperty("YamlContent")!.SetValue(model, "trigger: manual");

        var submit = typeof(TemplateEditDialog).GetMethod("OnSubmit", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await cut.InvokeAsync(() => (Task)submit.Invoke(cut.Instance, [])!);

        // A new template POSTs to the templates endpoint. (The dialog-close-with-true on success is a
        // OmniDialogService.Close a standalone bUnit render cannot observe.)
        Assert.Contains(_handler.Requests, r => r.Method == "POST" && r.Url.Contains("api/pipelines/templates"));
    }
}
