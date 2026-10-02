// SPDX-License-Identifier: EUPL-1.2
using System.Security.Claims;

namespace Aetheus.Back.Components.Dashboards;

/// <summary>
/// The caller's own dashboards. An identity without a user row (the deployment identity,
/// NameIdentifier "bootstrap") is signed in but owns none: it lists an empty set, finds nothing and
/// may not create or change one (403). It used to get 401, which the front reads as a rejected
/// session and signs out on, as the notification endpoints did for deploy-prod 2442 and 2443.
/// </summary>
[ApiController]
[Route("api/dashboards")]
[Authorize]
[NotResourceScoped("A dashboard belongs to the calling user: the service filters every id on their own user id, not on a tenant permission.")]
public class DashboardsController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<DashboardDto>>> GetDashboards(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Ok(new List<DashboardDto>());
        return Ok(await dashboardService.GetUserDashboardsAsync(userId.Value, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DashboardDto>> GetDashboard(int id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return NotFound();
        var result = await dashboardService.GetDashboardAsync(id, userId.Value, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<DashboardDto>> CreateDashboard([FromBody] CreateDashboardRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Forbid();
        var result = await dashboardService.CreateDashboardAsync(userId.Value, request, ct);
        return CreatedAtAction(nameof(GetDashboard), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DashboardDto>> UpdateDashboard(int id, [FromBody] UpdateDashboardRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Forbid();
        var result = await dashboardService.UpdateDashboardAsync(id, userId.Value, request, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteDashboard(int id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return NotFound();
        var deleted = await dashboardService.DeleteDashboardAsync(id, userId.Value, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    private int? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}
