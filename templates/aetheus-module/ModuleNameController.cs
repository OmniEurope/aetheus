// SPDX-License-Identifier: EUPL-1.2
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aetheus.Back.Components.ModuleName;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ModuleNameController : ControllerBase
{
    // The scaffold fails honestly until the module contract is implemented. Never replace these with
    // empty HTTP 200 responses: that makes an unfinished module look production-ready.
    [HttpGet]
    public ActionResult GetAll() => StatusCode(
        StatusCodes.Status501NotImplemented,
        new ProblemDetails { Status = StatusCodes.Status501NotImplemented, Title = "ModuleName is not implemented." });

    [HttpGet("{id:int}")]
    public ActionResult GetById(int id) => StatusCode(
        StatusCodes.Status501NotImplemented,
        new ProblemDetails { Status = StatusCodes.Status501NotImplemented, Title = $"ModuleName lookup for {id} is not implemented." });
}
