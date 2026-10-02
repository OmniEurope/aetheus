// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;

namespace Aetheus.Back.Components.Notifications;

/// <summary>
/// The current user's notifications, preferences and project subscriptions. Any signed-in user, no
/// Admin role: every action is scoped to the caller's own user id, and a delivery that belongs to
/// someone else answers 404 exactly like one that does not exist. The admin channel and rule
/// endpoints stay on <see cref="NotificationsController"/>.
///
/// An identity without a user row (the self-expiring deployment identity, NameIdentifier
/// "bootstrap") is signed in but has no inbox: it reads an empty one, and what it cannot hold
/// (preferences, subscriptions) is refused with 403. It used to get 401, which the front reads as a
/// rejected session: the deployment smoke was signed out onto /login, and deploy-prod 2442 and 2443
/// rolled back on it.
/// </summary>
[ApiController]
[Route("api/notifications/me")]
[Authorize]
public sealed class UserNotificationsController(
    IUserNotificationService service,
    IResourceAuthorizationService authz) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<NotificationDeliveryDto>>> GetMine(
        [FromQuery] UserNotificationPageRequest request, CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId)
            return Ok(new PaginatedResult<NotificationDeliveryDto> { Page = request.Page, PageSize = request.PageSize });
        return Ok(await service.GetDeliveriesAsync(userId, request, ct));
    }

    /// <summary>Recette R-224: the event types the caller's /notifications Event filter offers.</summary>
    [HttpGet("filter-values")]
    public async Task<ActionResult<UserNotificationFilterValuesDto>> GetFilterValues(CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return Ok(new UserNotificationFilterValuesDto());
        return Ok(await service.GetFilterValuesAsync(userId, ct));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount(CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return Ok(0);
        return Ok(await service.GetUnreadCountAsync(userId, ct));
    }

    [HttpPost("{id:int}/read")]
    [NotResourceScoped("A delivery belongs to the calling user: the service only matches a delivery of their own user id, and answers 404 for anyone else's.")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return NotFound();
        return await service.MarkReadAsync(userId, id, ct) ? NoContent() : NotFound();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<int>> MarkAllRead(CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return Ok(0);
        return Ok(await service.MarkAllReadAsync(userId, ct));
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<List<NotificationPreferenceDto>>> GetPreferences(CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return Ok(new List<NotificationPreferenceDto>());
        return Ok(await service.GetPreferencesAsync(userId, ct));
    }

    [HttpPut("preferences")]
    public async Task<ActionResult<List<NotificationPreferenceDto>>> UpdatePreferences(
        [FromBody] UpdateNotificationPreferencesRequest request, CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return Forbid();
        return Ok(await service.UpdatePreferencesAsync(userId, request, ct));
    }

    [HttpGet("subscriptions")]
    public async Task<ActionResult<List<ProjectSubscriptionDto>>> GetSubscriptions(CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return Ok(new List<ProjectSubscriptionDto>());
        return Ok(await service.GetSubscriptionsAsync(userId, ct));
    }

    [HttpPut("subscriptions/{projectId:int}")]
    public async Task<ActionResult<ProjectSubscriptionDto>> Subscribe(int projectId, CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return Forbid();
        // Same check as reading the project on ProjectsController: following a project is reading it.
        if (!await authz.HasPermissionAsync(User, ResourceType.Project, projectId, Permission.Read, ct))
            return Forbid();

        var subscription = await service.SubscribeAsync(userId, projectId, ct);
        return subscription is null ? NotFound() : Ok(subscription);
    }

    /// <summary>
    /// Recette R2-034, "follow all my projects": follows every project the caller can read (the same
    /// Project/Read grant as following one project) and returns all of the caller's subscriptions.
    /// </summary>
    [HttpPost("subscriptions/all")]
    public async Task<ActionResult<List<ProjectSubscriptionDto>>> FollowAll(CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return Forbid();
        var readable = await authz.GetAccessibleResourceIdsAsync(User, ResourceType.Project, Permission.Read, ct);
        return Ok(await service.FollowProjectsAsync(userId, readable, ct));
    }

    [NotResourceScoped("Removes only the calling user's own subscription row; a user who lost access to the project must still be able to stop following it.")]
    [HttpDelete("subscriptions/{projectId:int}")]
    public async Task<IActionResult> Unsubscribe(int projectId, CancellationToken ct)
    {
        if (CurrentUserId() is not { } userId) return NotFound();
        return await service.UnsubscribeAsync(userId, projectId, ct) ? NoContent() : NotFound();
    }

    private int? CurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}
