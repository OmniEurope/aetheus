// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;
using Aetheus.Back.Components.Notifications;
using Aetheus.Back.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Aetheus.Back.Tests.Notifications;

/// <summary>
/// The deployment identity (NameIdentifier "bootstrap", no user row) is signed in. Answering it 401
/// made the front end the session, and the deployment smoke of deploy-prod 2442 and 2443 landed on
/// /login and rolled back. It now reads an empty inbox; nothing it asks for is a 401.
/// </summary>
public class UserNotificationsControllerIdentityTests
{
    private static UserNotificationsController Controller(IUserNotificationService service) => new(
        service, Substitute.For<IResourceAuthorizationService>())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "bootstrap")], "Bearer"))
            }
        }
    };

    [Fact]
    public async Task TheDeploymentIdentity_ReadsAnEmptyInbox_WithoutTouchingTheService()
    {
        var service = Substitute.For<IUserNotificationService>();
        var controller = Controller(service);

        var count = await controller.GetUnreadCount(CancellationToken.None);
        var page = await controller.GetMine(new UserNotificationPageRequest { Page = 1, PageSize = 5 }, CancellationToken.None);
        var filters = await controller.GetFilterValues(CancellationToken.None);
        var preferences = await controller.GetPreferences(CancellationToken.None);
        var subscriptions = await controller.GetSubscriptions(CancellationToken.None);

        Assert.Equal(0, Assert.IsType<OkObjectResult>(count.Result).Value);
        var inbox = Assert.IsType<PaginatedResult<NotificationDeliveryDto>>(Assert.IsType<OkObjectResult>(page.Result).Value);
        Assert.Empty(inbox.Items);
        Assert.Equal(0, inbox.TotalCount);
        Assert.Empty(Assert.IsType<UserNotificationFilterValuesDto>(Assert.IsType<OkObjectResult>(filters.Result).Value).EventTypes);
        Assert.Empty(Assert.IsType<List<NotificationPreferenceDto>>(Assert.IsType<OkObjectResult>(preferences.Result).Value));
        Assert.Empty(Assert.IsType<List<ProjectSubscriptionDto>>(Assert.IsType<OkObjectResult>(subscriptions.Result).Value));
        Assert.Empty(service.ReceivedCalls());
    }

    [Fact]
    public async Task TheDeploymentIdentity_IsNeverAnswered401()
    {
        var controller = Controller(Substitute.For<IUserNotificationService>());

        IActionResult[] results =
        [
            await controller.MarkRead(1, CancellationToken.None),
            (await controller.MarkAllRead(CancellationToken.None)).Result!,
            (await controller.UpdatePreferences(new UpdateNotificationPreferencesRequest(), CancellationToken.None)).Result!,
            (await controller.Subscribe(1, CancellationToken.None)).Result!,
            await controller.Unsubscribe(1, CancellationToken.None)
        ];

        Assert.DoesNotContain(results, result => result is UnauthorizedResult);
        Assert.IsType<ForbidResult>(results[2]);
        Assert.IsType<ForbidResult>(results[3]);
    }
}
