// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Bunit;

namespace Aetheus.Front.Tests.Shared;

public sealed class AppErrorsViewTests : BunitContext
{
    [Fact]
    public void MoreThanOnePage_RendersPagerAndRequestsBoundedPage()
    {
        var handler = BunitTestHelper.RegisterServices(this);
        handler.SetJsonResponse("api/appmonitoring/apps/7/errors", new PaginatedResult<AppErrorEventDto>
        {
            Items = [new AppErrorEventDto { Id = 1, ExceptionType = "InvalidOperationException", Message = "failure" }],
            TotalCount = 30,
            Page = 1,
            PageSize = 25
        });

        var cut = Render<AppErrorsView>(parameters => parameters.Add(component => component.AppId, 7));

        Assert.Contains("rz-pager", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(handler.Requests, request =>
            request.Url.Contains("page=1", StringComparison.Ordinal)
            && request.Url.Contains("pageSize=25", StringComparison.Ordinal));
    }
}
