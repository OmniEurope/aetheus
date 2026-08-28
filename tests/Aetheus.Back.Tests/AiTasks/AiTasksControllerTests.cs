// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.AiTasks;
using Aetheus.Back.Components.Pipelines;
using Aetheus.Back.Components.Tasks;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests.AiTasks;

public sealed class AiTasksControllerTests
{
    private readonly IAiTaskService _service = Substitute.For<IAiTaskService>();
    private readonly IResourceAuthorizationService _authorization =
        Substitute.For<IResourceAuthorizationService>();
    // The run-scoped RBAC lookup is an own-read on this module's repository now.
    // The run-scoped RBAC lookup goes through the service, like every other controller read.
    private readonly ITaskService _tasks = Substitute.For<ITaskService>();
    private readonly AiTasksController _controller;

    public AiTasksControllerTests()
    {
        _authorization.HasPermissionAsync(
                Arg.Any<ClaimsPrincipal>(),
                Arg.Any<ResourceType>(),
                Arg.Any<int?>(),
                Arg.Any<Permission>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        _authorization.GetAccessibleResourceIdsAsync(
                Arg.Any<ClaimsPrincipal>(),
                Arg.Any<ResourceType>(),
                Arg.Any<Permission>(),
                Arg.Any<CancellationToken>())
            .Returns([1, 2]);
        _controller = new AiTasksController(
            _service,
            _authorization,
            _tasks)
        {
            ControllerContext = Context(
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, "Admin"))
        };
    }

    [Fact]
    public async Task ProfileEndpoints_ReturnExpectedHttpResults()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = new AiRunnerProfileDto { Id = 3, Name = "reviewer" };
        var page = new PaginatedResult<AiRunnerProfileDto>
        {
            Items = [profile],
            TotalCount = 1
        };
        _service.GetProfilesAsync(Arg.Any<PaginationRequest>(), ct).Returns(page);
        _service.GetProfileAsync(3, ct).Returns(profile);
        _service.GetProfileAsync(404, ct).Returns((AiRunnerProfileDto?)null);
        _service.CreateProfileAsync(Arg.Any<CreateAiRunnerProfileRequest>(), ct)
            .Returns(profile);
        _service.UpdateProfileAsync(3, Arg.Any<UpdateAiRunnerProfileRequest>(), ct)
            .Returns(profile);
        _service.UpdateProfileAsync(404, Arg.Any<UpdateAiRunnerProfileRequest>(), ct)
            .Returns((AiRunnerProfileDto?)null);
        _service.DeleteProfileAsync(3, ct).Returns(true);
        _service.DeleteProfileAsync(404, ct).Returns(false);

        Assert.IsType<OkObjectResult>(
            (await _controller.GetProfiles(new PaginationRequest(), ct)).Result);
        Assert.IsType<OkObjectResult>((await _controller.GetProfileOptions(ct)).Result);
        Assert.IsType<OkObjectResult>((await _controller.GetProfile(3, ct)).Result);
        Assert.IsType<NotFoundResult>((await _controller.GetProfile(404, ct)).Result);
        Assert.IsType<CreatedAtActionResult>(
            (await _controller.CreateProfile(new CreateAiRunnerProfileRequest(), ct)).Result);
        Assert.IsType<OkObjectResult>(
            (await _controller.UpdateProfile(3, new UpdateAiRunnerProfileRequest(), ct)).Result);
        Assert.IsType<NotFoundResult>(
            (await _controller.UpdateProfile(404, new UpdateAiRunnerProfileRequest(), ct)).Result);
        Assert.IsType<NoContentResult>(await _controller.DeleteProfile(3, ct));
        Assert.IsType<NotFoundResult>(await _controller.DeleteProfile(404, ct));
    }

    [Fact]
    public async Task ProfileOptions_NonAdmin_UsesAuthorizedOrganizations()
    {
        var ct = TestContext.Current.CancellationToken;
        _controller.ControllerContext = Context(new Claim(ClaimTypes.Name, "member"));
        _authorization.GetUserOrganizationIdsAsync(
                Arg.Any<ClaimsPrincipal>(),
                ct)
            .Returns([7]);
        _service.GetProfileOptionsAsync(
                Arg.Is<List<int>>(ids => ids.SequenceEqual(new[] { 7 })),
                ct)
            .Returns([new AiRunnerProfileDto { Id = 3 }]);

        Assert.IsType<OkObjectResult>((await _controller.GetProfileOptions(ct)).Result);
        await _service.Received(1).GetProfileOptionsAsync(
            Arg.Is<List<int>>(ids => ids.SequenceEqual(new[] { 7 })),
            ct);
    }

    [Fact]
    public async Task DefinitionEndpoints_ExerciseAuthorizedCrudAndRun()
    {
        var ct = TestContext.Current.CancellationToken;
        var definition = Definition();
        _service.GetDefinitionsAsync(
                Arg.Any<PaginationRequest>(),
                12,
                null,
                Arg.Any<List<int>?>(),
                Arg.Any<List<int>?>(),
                ct)
            .Returns(new PaginatedResult<AiTaskDefinitionDto> { Items = [definition] });
        _service.GetDefinitionAsync(11, ct).Returns(definition);
        _service.GetDefinitionAsync(404, ct).Returns((AiTaskDefinitionDto?)null);
        _service.CreateDefinitionAsync(Arg.Any<CreateAiTaskDefinitionRequest>(), ct)
            .Returns(definition);
        _service.UpdateDefinitionAsync(11, Arg.Any<UpdateAiTaskDefinitionRequest>(), ct)
            .Returns(definition);
        _service.DeleteDefinitionAsync(11, ct).Returns(true);
        _service.RunNowAsync(11, null, ct).Returns(new ServerTask { Id = 91 });

        Assert.IsType<OkObjectResult>(
            (await _controller.GetDefinitions(12, null, new PaginationRequest(), ct)).Result);
        Assert.IsType<OkObjectResult>((await _controller.GetDefinition(11, ct)).Result);
        Assert.IsType<NotFoundResult>((await _controller.GetDefinition(404, ct)).Result);
        Assert.IsType<CreatedAtActionResult>(
            (await _controller.CreateDefinition(
                new CreateAiTaskDefinitionRequest { ProjectId = 12 },
                ct)).Result);
        Assert.IsType<OkObjectResult>(
            (await _controller.UpdateDefinition(
                11,
                new UpdateAiTaskDefinitionRequest { ProjectId = 12 },
                ct)).Result);
        Assert.IsType<NoContentResult>(await _controller.DeleteDefinition(11, ct));
        var run = Assert.IsType<OkObjectResult>((await _controller.RunNow(11, ct)).Result);
        Assert.Equal(91, Assert.IsType<AiRunStartDto>(run.Value).TaskId);
    }

    [Fact]
    public async Task DefinitionMutations_ForbidBeforeCallingService()
    {
        var ct = TestContext.Current.CancellationToken;
        var definition = Definition();
        _service.GetDefinitionAsync(11, ct).Returns(definition);
        _authorization.HasPermissionAsync(
                Arg.Any<ClaimsPrincipal>(),
                ResourceType.Project,
                12,
                Arg.Any<Permission>(),
                ct)
            .Returns(false);

        Assert.IsType<ForbidResult>(
            (await _controller.CreateDefinition(
                new CreateAiTaskDefinitionRequest { ProjectId = 12 },
                ct)).Result);
        Assert.IsType<ForbidResult>(
            (await _controller.UpdateDefinition(
                11,
                new UpdateAiTaskDefinitionRequest { ProjectId = 12 },
                ct)).Result);
        Assert.IsType<ForbidResult>(await _controller.DeleteDefinition(11, ct));
        Assert.IsType<ForbidResult>((await _controller.RunNow(11, ct)).Result);
    }

    [Fact]
    public async Task ResultConsumptionAndAgentEndpoints_ReturnAuthorizedResults()
    {
        var ct = TestContext.Current.CancellationToken;
        var definition = Definition();
        var result = new AiRunResultDto { Id = 88, ProjectId = 12 };
        var request = new PublishAiRunResultRequest
        {
            ServerTaskId = 91,
            ProfileName = "reviewer",
            ReportMarkdown = "report"
        };
        _service.GetDefinitionAsync(11, ct).Returns(definition);
        _service.GetResultsAsync(11, null, Arg.Any<PaginationRequest>(), ct)
            .Returns(new PaginatedResult<AiRunResultDto> { Items = [result] });
        _service.GetResultAsync(88, ct).Returns(result);
        _service.ApplyProposedPatchAsync(88, ct)
            .Returns(new AiPatchApplicationDto
            {
                RepositoryId = 13,
                BranchName = "ai-proposed/88",
                CommitSha = new string('a', 40)
            });
        _service.GetConsumptionAsync(12, ct).Returns(new AiConsumptionDto { RunCount = 3 });
        _tasks.GetTaskServerIdAsync(91, ct).Returns(9);
        _service.PublishResultAsync(request, ct).Returns(result);
        _controller.ControllerContext = Context(
            new Claim(ClaimTypes.Name, "agent"),
            new Claim("ServerId", "9"));

        Assert.IsType<OkObjectResult>(
            (await _controller.GetResults(11, null, new PaginationRequest(), ct)).Result);
        Assert.IsType<OkObjectResult>((await _controller.ApplyProposedPatch(88, ct)).Result);
        Assert.IsType<OkObjectResult>((await _controller.GetConsumption(12, ct)).Result);
        Assert.IsType<OkObjectResult>((await _controller.PublishResult(request, ct)).Result);
    }

    [Fact]
    public async Task ResultEndpoints_RejectMissingScopeAndWrongAgent()
    {
        var ct = TestContext.Current.CancellationToken;
        _controller.ControllerContext = Context(new Claim(ClaimTypes.Name, "member"));
        _service.GetDefinitionAsync(404, ct).Returns((AiTaskDefinitionDto?)null);
        _service.GetResultAsync(404, ct).Returns((AiRunResultDto?)null);
        _tasks.GetTaskServerIdAsync(91, ct).Returns(9);
        var request = new PublishAiRunResultRequest
        {
            ServerTaskId = 91,
            ProfileName = "reviewer",
            ReportMarkdown = "report"
        };

        Assert.IsType<NotFoundResult>(
            (await _controller.GetResults(404, null, new PaginationRequest(), ct)).Result);
        Assert.IsType<ForbidResult>(
            (await _controller.GetResults(null, null, new PaginationRequest(), ct)).Result);
        Assert.IsType<NotFoundResult>((await _controller.ApplyProposedPatch(404, ct)).Result);
        Assert.IsType<ForbidResult>((await _controller.GetConsumption(null, ct)).Result);
        Assert.IsType<ForbidResult>((await _controller.PublishResult(request, ct)).Result);
    }

    private static AiTaskDefinitionDto Definition() => new()
    {
        Id = 11,
        Name = "Review",
        ProfileId = 3,
        ProjectId = 12
    };

    private static ControllerContext Context(params Claim[] claims) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        }
    };
}
