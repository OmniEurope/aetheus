// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.Servers.ServerDetailSections;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerLogsSectionRenderTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public ServerLogsSectionRenderTests()
    {
        _handler = BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_EmptyLogs()
    {
        _handler.SetJsonResponse("api/servers/1/logs", new PaginatedResult<TaskLogDto> { Items = [], TotalCount = 0 });
        var cut = Render<ServerLogsSection>(p => p.Add(x => x.ServerId, 1));
        // Once the fetch resolves, the logs header and the empty-state text render.
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Logs", cut.Markup);
            Assert.Contains("NoLogs", cut.Markup);
        });
    }

    [Fact]
    public void Renders_WithLogs()
    {
        _handler.SetJsonResponse("api/servers/1/logs", new PaginatedResult<TaskLogDto>
        {
            Items =
            [
                new TaskLogDto { Id = 1, Message = "Build started", Level = TaskLogLevel.Info, Timestamp = DateTime.UtcNow }
            ],
            TotalCount = 1
        });
        var cut = Render<ServerLogsSection>(p => p.Add(x => x.ServerId, 1));
        // Once the fetch resolves, the seeded log message renders in the grid.
        cut.WaitForAssertion(() => Assert.Contains("Build started", cut.Markup));
    }
}
