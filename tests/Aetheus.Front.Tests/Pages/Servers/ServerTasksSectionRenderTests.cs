// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Aetheus.Front.Components.Tasks;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerTasksSectionRenderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerTasksSectionRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_SharedTaskListView()
    {
        _handler.SetJsonResponse("api/tasks", new PaginatedResult<ServerTaskDto> { Items = [], TotalCount = 0 });
        var server = new ServerDetailDto { Id = 1, Name = "web-01" };

        var cut = Render<ServerTasksSection>(p => p.Add(x => x.Server, server));

        Assert.NotNull(cut.FindComponent<TaskListView>());
    }
}
