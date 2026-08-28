// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Pipelines;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Aetheus.Front.Tests.Pages.Pipelines;

public sealed class PipelineSetupWizardTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public PipelineSetupWizardTests() => _handler = BunitTestHelper.RegisterServices(this);

    [Fact]
    public async Task Wizard_PreselectsProjectAndSummarizesTheGeneratedPipelineSet()
    {
        _handler.SetPaginatedJsonResponse("api/projects",
        [
            new ProjectDto { Id = 7, Name = "Atlas", DefaultBranch = "develop" }
        ]);
        _handler.SetJsonResponse("api/pipelines/templates", TemplateCatalog());

        Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/pipelines/setup?projectId=7");
        var cut = Render<PipelineSetupWizard>();
        cut.WaitForAssertion(() => Assert.Contains("Atlas", cut.Markup, StringComparison.Ordinal));
        var wizard = cut.FindComponent<WizardDialog>();

        await cut.InvokeAsync(wizard.Instance.NextStep);
        await cut.InvokeAsync(wizard.Instance.NextStep);
        await cut.InvokeAsync(wizard.Instance.NextStep);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("atlas-ci", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("application-ci", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("atlas-deploy-prod", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("6 Pipelines", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Wizard_FinishCreatesTheCompleteCoherentDefaultPipelineSet()
    {
        _handler.SetPaginatedJsonResponse("api/projects",
        [
            new ProjectDto { Id = 7, Name = "Atlas", DefaultBranch = "develop" }
        ]);
        _handler.SetJsonResponse("api/pipelines/templates", TemplateCatalog());
        _handler.SetJsonResponse(HttpMethod.Post, "api/pipelines", new PipelineDto { Id = 91 });
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo("http://localhost/pipelines/setup?projectId=7");
        var cut = Render<PipelineSetupWizard>();
        cut.WaitForAssertion(() => Assert.Contains("Atlas", cut.Markup, StringComparison.Ordinal));
        var wizard = cut.FindComponent<WizardDialog>();

        await cut.InvokeAsync(wizard.Instance.NextStep);
        await cut.InvokeAsync(wizard.Instance.NextStep);
        await cut.InvokeAsync(wizard.Instance.NextStep);
        await cut.Find("button.rz-primary").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(6, _handler.RequestDetails.Count(request =>
            request.Method == HttpMethod.Post.Method
            && request.Url.EndsWith("api/pipelines", StringComparison.OrdinalIgnoreCase))));
        var requests = _handler.RequestDetails
            .Where(request => request.Method == HttpMethod.Post.Method
                && request.Url.EndsWith("api/pipelines", StringComparison.OrdinalIgnoreCase))
            .Select(request => JsonSerializer.Deserialize<CreatePipelineRequest>(request.Body!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .ToArray();

        Assert.Equal(
            ["atlas-ci", "atlas-quality", "atlas-security", "atlas-qa", "atlas-candidate", "atlas-deploy-prod"],
            requests.Select(request => request.Name));
        Assert.All(requests, request =>
        {
            Assert.Equal(7, request.ProjectId);
            Assert.Equal("develop", request.SourceBranch);
            Assert.Contains("APPLICATION_HAS_FRONTEND: \"true\"", request.YamlDefinition, StringComparison.Ordinal);
            Assert.Contains("APPLICATION_HAS_BACKEND: \"true\"", request.YamlDefinition, StringComparison.Ordinal);
            Assert.Contains("APPLICATION_HAS_DATABASE: \"false\"", request.YamlDefinition, StringComparison.Ordinal);
        });
        Assert.Contains("extends: application-candidate@1", requests[4].YamlDefinition, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_SECURITY_ENABLED: \"true\"", requests[4].YamlDefinition, StringComparison.Ordinal);
        Assert.Contains("extends: application-deploy-prod@1", requests[5].YamlDefinition, StringComparison.Ordinal);
    }

    private static List<PipelineTemplateSummaryDto> TemplateCatalog() =>
    [
        Template("application-ci"),
        Template("application-quality"),
        Template("application-security"),
        Template("application-qa"),
        Template("application-candidate"),
        Template("application-nightly"),
        Template("application-deploy-prod"),
        Template("application-release-fast"),
        Template("package-telemetry"),
        Template("package-web-analytics-dotnet"),
        Template("package-web-analytics-browser"),
        Template("publish-observability-packages")
    ];

    private static PipelineTemplateSummaryDto Template(string name) => new()
    {
        Id = name.GetHashCode(StringComparison.Ordinal),
        Name = name,
        Version = 1
    };
}
