// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.PackageFeeds;

[ApiController]
[Route("api/package-feeds")]
[Authorize(Roles = "Admin")]
public class PackageFeedsController(IPackageFeedService feedService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<PackageFeedDto>>> GetFeeds(
        [FromQuery] int? projectId, [FromQuery] PaginationRequest request, CancellationToken ct)
    {
        return Ok(await feedService.GetFeedsAsync(projectId, request, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PackageFeedDetailDto>> GetFeed(int id, CancellationToken ct)
    {
        var result = await feedService.GetFeedDetailAsync(id, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<PackageFeedDto>> CreateFeed([FromBody] CreatePackageFeedRequest request, CancellationToken ct)
    {
        var result = await feedService.CreateFeedAsync(request, ct);
        return CreatedAtAction(nameof(GetFeed), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PackageFeedDto>> UpdateFeed(int id, [FromBody] UpdatePackageFeedRequest request, CancellationToken ct)
    {
        var result = await feedService.UpdateFeedAsync(id, request, ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteFeed(int id, CancellationToken ct)
    {
        var deleted = await feedService.DeleteFeedAsync(id, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:int}/packages")]
    public async Task<ActionResult<PackageEntryDto>> AddPackage(int id, [FromBody] AddPackageRequest request, CancellationToken ct)
    {
        var result = await feedService.AddPackageAsync(id, request, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:int}/packages/{packageId:int}")]
    public async Task<IActionResult> RemovePackage(int id, int packageId, CancellationToken ct)
    {
        return await feedService.RemovePackageAsync(id, packageId, ct) ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/sync")]
    public async Task<ActionResult<PackageFeedSyncResultDto>> SyncFeed(int id, CancellationToken ct)
    {
        var result = await feedService.SyncFeedAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
