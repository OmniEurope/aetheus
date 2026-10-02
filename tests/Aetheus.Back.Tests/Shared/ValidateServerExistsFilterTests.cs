// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Shared;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Aetheus.Back.Tests;

public class ValidateServerExistsFilterTests
{
    [Fact]
    public async Task ServerExists_CallsNext()
    {
        await using var db = CreateDb();
        db.Servers.Add(new Server { Id = 1, Name = "s1", Hostname = "s1" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var filter = new ValidateServerExistsFilter(new ServerExistenceRepository(db));

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
        await using var db = CreateDb();
        var filter = new ValidateServerExistsFilter(new ServerExistenceRepository(db));

        var context = CreateContext(new Dictionary<string, object?> { { "serverId", 999 } });

        await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], null!)));

        Assert.IsType<NotFoundObjectResult>(context.Result);
    }

    [Fact]
    public async Task NoServerIdArg_CallsNext()
    {
        await using var db = CreateDb();
        var filter = new ValidateServerExistsFilter(new ServerExistenceRepository(db));

        var nextCalled = false;
        var context = CreateContext(new Dictionary<string, object?>());

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], null!));
        });

        Assert.True(nextCalled);
    }

    // The filter reads the shared Servers table itself (layer guard, 2026-09-25): a real in-memory
    // database proves the query, where a substitute only proved the call.
    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options,
        TimeProvider.System);

    private static ActionExecutingContext CreateContext(Dictionary<string, object?> actionArguments)
    {
        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        var actionDescriptor = new ActionDescriptor();
        var actionContext = new ActionContext(httpContext, routeData, actionDescriptor);

        return new ActionExecutingContext(actionContext, [], actionArguments, null!);
    }
}
