// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aetheus.Back.Components.Shared;

public class ValidateServerExistsFilter(ServerExistenceRepository serverRepo) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionArguments.TryGetValue("serverId", out var obj) && obj is int serverId)
        {
            if (!await serverRepo.ServerExistsAsync(serverId, context.HttpContext.RequestAborted).ConfigureAwait(false))
            {
                context.Result = new NotFoundObjectResult(new ApiError { Message = $"Server {serverId} not found." });
                return;
            }
        }

        await next();
    }
}
