// SPDX-License-Identifier: EUPL-1.2
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Aetheus.Front.Components.AiTasks;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Tests.Pages.Ai;

/// <summary>
/// The two AI edit dialogs, end to end through their real forms: what they load, what they send,
/// and how they behave when the backend refuses. Both were entirely unexercised, so the owner
/// switch, the comma/newline parsing and the failure paths had no protection at all.
/// </summary>
public sealed class AiDialogTests : BunitContext
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BunitTestHelper.TestHandler _handler;

    public AiDialogTests()
    {
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);
        _handler.SetJsonResponse("api/ai/profile-options", new List<AiRunnerProfileDto>
        {
            new() { Id = 3, Name = "claude" },
            new() { Id = 4, Name = "codex" }
        });
        _handler.SetJsonResponse("api/projects", new PaginatedResult<ProjectDto>
        {
            Items = [new ProjectDto { Id = 10, Name = "aetheus" }],
            TotalCount = 1,
            Page = 1,
            PageSize = 100
        });
        _handler.SetJsonResponse("api/servers", new PaginatedResult<ServerDto>
        {
            Items = [new ServerDto { Id = 20, Name = "runner" }],
            TotalCount = 1,
            Page = 1,
            PageSize = 100
        });
    }

    private T LastBody<T>(string method, string urlContains)
    {
        var body = _handler.RequestDetails
            .Last(request => request.Method == method
                && request.Url.Contains(urlContains, StringComparison.Ordinal))
            .Body;
        return JsonSerializer.Deserialize<T>(body!, Json)!;
    }

    /// <summary>
    /// Neither dialog gives its inputs a <c>name</c>, so a field is addressed the way a user
    /// sees it: by the label its <c>OmniFormField</c> renders. Both controls bind on <c>change</c>.
    /// </summary>
    private static IElement Field<TComponent>(
        IRenderedComponent<TComponent> cut, string label, string tag = "input")
        where TComponent : IComponent =>
        cut.FindAll(".rz-form-field, .omni-form-field")
            .First(field => field.QuerySelector("label")?.TextContent.Trim() == label)
            .QuerySelector(tag)!;

    private bool Sent(string method, string urlContains) =>
        _handler.Requests.Any(request => request.Method == method
            && request.Url.Contains(urlContains, StringComparison.Ordinal));

    // ---------- AiTaskDialog ----------

    [Fact]
    public void TaskDialog_InCreateMode_PreselectsTheFirstProfileAndOffersBothOwnerKinds()
    {
        var cut = Render<AiTaskDialog>();

        cut.WaitForAssertion(() => Assert.Contains("Create", cut.Markup), TimeSpan.FromSeconds(2));
        Assert.Contains("OwnerType", cut.Markup);
        Assert.Contains("Server", cut.Markup);
    }

    [Fact]
    public void TaskDialog_CreatesAServerOwnedTaskWithTheParsedEventTypes()
    {
        _handler.SetJsonResponse(
            HttpMethod.Post, "api/ai/tasks", new AiTaskDefinitionDto { Id = 1, Name = "review" });

        var cut = Render<AiTaskDialog>();
        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));

        Field(cut, "Name").Input("review");
        Field(cut, "PromptTemplate", "textarea").Input("Review {diff}");
        Field(cut, "EventTypes").Input(" pipeline.completed , release.created ,, ");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.True(Sent("POST", "api/ai/tasks")), TimeSpan.FromSeconds(2));
        var request = LastBody<CreateAiTaskDefinitionRequest>("POST", "api/ai/tasks");
        Assert.Equal("review", request.Name);
        Assert.Equal("Review {diff}", request.PromptTemplate);
        Assert.Equal(3, request.ProfileId);
        Assert.Null(request.ProjectId);
        Assert.Equal(["pipeline.completed", "release.created"], request.EventTypes);
    }

    [Fact]
    public void TaskDialog_TurnsABlankScheduleIntoNoScheduleAtAll()
    {
        _handler.SetJsonResponse(
            HttpMethod.Post, "api/ai/tasks", new AiTaskDefinitionDto { Id = 1, Name = "review" });

        var cut = Render<AiTaskDialog>();
        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));

        Field(cut, "Name").Input("review");
        Field(cut, "PromptTemplate", "textarea").Input("Review");
        Field(cut, "Schedule").Input("   ");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.True(Sent("POST", "api/ai/tasks")), TimeSpan.FromSeconds(2));
        Assert.Null(LastBody<CreateAiTaskDefinitionRequest>("POST", "api/ai/tasks").Schedule);
    }

    [Fact]
    public void TaskDialog_WithAFixedProjectOwnsTheTaskByThatProjectAndHidesTheOwnerSwitch()
    {
        _handler.SetJsonResponse(
            HttpMethod.Post, "api/ai/tasks", new AiTaskDefinitionDto { Id = 1, Name = "review" });

        var cut = Render<AiTaskDialog>(
            parameters => parameters.Add(dialog => dialog.FixedProjectId, 10));
        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));

        Assert.DoesNotContain("OwnerType", cut.Markup);
        Field(cut, "Name").Input("review");
        Field(cut, "PromptTemplate", "textarea").Input("Review");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.True(Sent("POST", "api/ai/tasks")), TimeSpan.FromSeconds(2));
        var request = LastBody<CreateAiTaskDefinitionRequest>("POST", "api/ai/tasks");
        Assert.Equal(10, request.ProjectId);
        Assert.Null(request.ServerId);
    }

    [Fact]
    public void TaskDialog_InEditModeLoadsTheDefinitionAndSendsAnUpdate()
    {
        _handler.SetJsonResponse("api/ai/tasks/7", new AiTaskDefinitionDto
        {
            Id = 7,
            Name = "nightly review",
            ProfileId = 4,
            PromptTemplate = "Review {diff}",
            ProjectId = 10,
            Schedule = "0 2 * * *",
            EventTypes = ["pipeline.completed"],
            Enabled = false
        });
        _handler.SetJsonResponse(
            HttpMethod.Put, "api/ai/tasks/7", new AiTaskDefinitionDto { Id = 7, Name = "nightly review" });

        var cut = Render<AiTaskDialog>(
            parameters => parameters.Add(dialog => dialog.DefinitionId, 7));
        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));

        Assert.Contains("Save", cut.Markup);
        Assert.Equal("nightly review", Field(cut, "Name").GetAttribute("value"));
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.True(Sent("PUT", "api/ai/tasks/7")), TimeSpan.FromSeconds(2));
        var request = LastBody<UpdateAiTaskDefinitionRequest>("PUT", "api/ai/tasks/7");
        Assert.Equal("nightly review", request.Name);
        Assert.Equal(4, request.ProfileId);
        Assert.Equal(10, request.ProjectId);
        Assert.Equal("0 2 * * *", request.Schedule);
        Assert.Equal(["pipeline.completed"], request.EventTypes);
        Assert.False(request.Enabled);
    }

    [Fact]
    public void TaskDialog_StillRendersItsFormWhenTheOptionLoadFails()
    {
        _handler.SetResponse("api/ai/profile-options", HttpStatusCode.InternalServerError);

        var cut = Render<AiTaskDialog>();

        cut.WaitForAssertion(() => Assert.Contains("Create", cut.Markup), TimeSpan.FromSeconds(2));
    }

    // ---------- AiRunnerProfileDialog ----------

    [Fact]
    public void ProfileDialog_InCreateModeDoesNotCallTheBackendBeforeSubmit()
    {
        var cut = Render<AiRunnerProfileDialog>();

        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));
        Assert.DoesNotContain(
            _handler.Requests,
            request => request.Url.Contains("api/ai/profiles", StringComparison.Ordinal));
    }

    [Fact]
    public void ProfileDialog_SplitsTheArgumentLinesAndParsesTheEnvironmentPairs()
    {
        _handler.SetJsonResponse(
            HttpMethod.Post, "api/ai/profiles", new AiRunnerProfileDto { Id = 1, Name = "claude" });

        var cut = Render<AiRunnerProfileDialog>();
        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));

        Field(cut, "Name").Input("claude");
        Field(cut, "Binary").Input("/usr/bin/claude");
        Field(cut, "ArgumentTemplate", "textarea").Input("-p\n\n{prompt_file}\n");
        Field(cut, "EnvironmentVariables", "textarea").Input("KEY=value\nURL=https://x/y?a=b\n");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.True(Sent("POST", "api/ai/profiles")), TimeSpan.FromSeconds(2));
        var request = LastBody<CreateAiRunnerProfileRequest>("POST", "api/ai/profiles");
        Assert.Equal("claude", request.Name);
        Assert.Equal("/usr/bin/claude", request.Binary);
        Assert.Equal(["-p", "{prompt_file}"], request.ArgsTemplate);
        Assert.Equal("value", request.Environment["KEY"]);
        Assert.Equal("https://x/y?a=b", request.Environment["URL"]);
    }

    [Fact]
    public void ProfileDialog_RefusesAnEnvironmentLineWithoutAnEqualsSign()
    {
        var cut = Render<AiRunnerProfileDialog>();
        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));

        Field(cut, "Name").Input("claude");
        Field(cut, "Binary").Input("/usr/bin/claude");
        Field(cut, "EnvironmentVariables", "textarea").Input("NOT_A_PAIR");
        cut.Find("form").Submit();

        cut.WaitForAssertion(
            () => Assert.False(Sent("POST", "api/ai/profiles")),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ProfileDialog_RefusesAnEnvironmentLineStartingWithAnEqualsSign()
    {
        var cut = Render<AiRunnerProfileDialog>();
        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));

        Field(cut, "Name").Input("claude");
        Field(cut, "Binary").Input("/usr/bin/claude");
        Field(cut, "EnvironmentVariables", "textarea").Input("=orphan");
        cut.Find("form").Submit();

        cut.WaitForAssertion(
            () => Assert.False(Sent("POST", "api/ai/profiles")),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ProfileDialog_InEditModeLoadsTheProfileAndSendsAnUpdate()
    {
        _handler.SetJsonResponse("api/ai/profiles/5", new AiRunnerProfileDto
        {
            Id = 5,
            Name = "codex",
            Description = "the reviewer",
            Binary = "/usr/bin/codex",
            ArgsTemplate = ["-p", "{prompt_file}"],
            Environment = new Dictionary<string, string> { ["KEY"] = "value" },
            TimeoutSeconds = 900,
            MaxOutputBytes = 300_000,
            SendsDataExternally = true
        });
        _handler.SetJsonResponse(
            HttpMethod.Put, "api/ai/profiles/5", new AiRunnerProfileDto { Id = 5, Name = "codex" });

        var cut = Render<AiRunnerProfileDialog>(
            parameters => parameters.Add(dialog => dialog.ProfileId, 5));
        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));

        Assert.Contains("Save", cut.Markup);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.True(Sent("PUT", "api/ai/profiles/5")), TimeSpan.FromSeconds(2));
        var request = LastBody<UpdateAiRunnerProfileRequest>("PUT", "api/ai/profiles/5");
        Assert.Equal("codex", request.Name);
        Assert.Equal("the reviewer", request.Description);
        Assert.Equal("/usr/bin/codex", request.Binary);
        Assert.Equal(["-p", "{prompt_file}"], request.ArgsTemplate);
        Assert.Equal("value", request.Environment["KEY"]);
        Assert.Equal(900, request.TimeoutSeconds);
        Assert.Equal(300_000, request.MaxOutputBytes);
        Assert.True(request.SendsDataExternally);
    }

    [Fact]
    public void ProfileDialog_StillRendersItsFormWhenTheProfileLoadFails()
    {
        _handler.SetResponse("api/ai/profiles/5", HttpStatusCode.InternalServerError);

        var cut = Render<AiRunnerProfileDialog>(
            parameters => parameters.Add(dialog => dialog.ProfileId, 5));

        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ProfileDialog_KeepsTheDialogOpenWhenTheBackendRejectsTheCreate()
    {
        _handler.SetJsonResponse(
            HttpMethod.Post,
            "api/ai/profiles",
            (AiRunnerProfileDto?)null,
            HttpStatusCode.BadRequest);

        var cut = Render<AiRunnerProfileDialog>();
        cut.WaitForAssertion(() => cut.Find("form"), TimeSpan.FromSeconds(2));

        Field(cut, "Name").Input("claude");
        Field(cut, "Binary").Input("/usr/bin/claude");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.True(Sent("POST", "api/ai/profiles")), TimeSpan.FromSeconds(2));
        Assert.NotNull(cut.Find("form"));
    }
}
