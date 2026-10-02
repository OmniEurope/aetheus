// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.PersonalAccessTokens;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests.PersonalAccessTokens;

public sealed class PersonalAccessTokensControllerTests
{
    [Fact]
    public async Task GetMine_ForwardsPaginationForAuthenticatedUser()
    {
        var service = Substitute.For<IPersonalAccessTokenService>();
        var page = new PaginatedResult<PersonalAccessTokenDto>
        {
            Items = [new PersonalAccessTokenDto { Id = 3, Name = "ci-token" }],
            TotalCount = 21,
            Page = 2,
            PageSize = 10
        };
        service.GetForUserAsync(7, Arg.Any<PaginationRequest>(), Arg.Any<CancellationToken>())
            .Returns(page);
        var controller = new PersonalAccessTokensController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "7")], "test"))
                }
            }
        };
        var request = new PaginationRequest
        {
            Search = "ci",
            Page = 2,
            PageSize = 10,
            SortBy = "Name",
            SortDescending = true
        };

        var action = await controller.GetMine(request, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Same(page, ok.Value);
        await service.Received(1).GetForUserAsync(7, Arg.Is<PaginationRequest>(value =>
            value.Search == "ci" && value.Page == 2 && value.PageSize == 10 &&
            value.SortBy == "Name" && value.SortDescending), Arg.Any<CancellationToken>());
    }
}
