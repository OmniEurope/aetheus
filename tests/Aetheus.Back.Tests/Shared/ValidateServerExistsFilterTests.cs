// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Components.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Aetheus.Back.Tests;

public class ValidateServerExistsFilterTests
{
    [Fact]
    public async Task ServerExists_CallsNext()
    {
        var repoMock = Substitute.For<IServerRepository>();
        repoMock.ServerExistsAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        var filter = new ValidateServerExistsFilter(repoMock);

        var nextCalled = false;
        var context = CreateContext(new Dictionary<string, object?> { { "serverId", 1 } });

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], null!));
        });

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task ServerNotFound_ReturnsNotFound()
    {
        var repoMock = Substitute.For<IServerRepository>();
        repoMock.ServerExistsAsync(999, Arg.Any<CancellationToken>()).Returns(false);
        var filter = new ValidateServerExistsFilter(repoMock);

        var context = CreateContext(new Dictionary<string, object?> { { "serverId", 999 } });

        await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], null!)));

        Assert.IsType<NotFoundObjectResult>(context.Result);
    }

    [Fact]
    public async Task NoServerIdArg_CallsNext()
    {
        var repoMock = Substitute.For<IServerRepository>();
        var filter = new ValidateServerExistsFilter(repoMock);

        var nextCalled = false;
        var context = CreateContext(new Dictionary<string, object?>());

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], null!));
        });

        Assert.True(nextCalled);
    }

    private static ActionExecutingContext CreateContext(Dictionary<string, object?> actionArguments)
    {
        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        var actionDescriptor = new ActionDescriptor();
        var actionContext = new ActionContext(httpContext, routeData, actionDescriptor);

        return new ActionExecutingContext(actionContext, [], actionArguments, null!);
    }
}
