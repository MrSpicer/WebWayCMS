using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Controllers.Admin.Handlers;

namespace WebWayCMS.Core.Tests;

/// <summary>
/// Minimal IAdminRegistryHandler that does NOT override GetForm, so the default
/// interface implementation (returns NotFoundResult) is executed for coverage.
/// </summary>
internal sealed class MinimalRegistryHandler : IAdminRegistryHandler
{
    public IActionResult GetAll() => new JsonResult(new { });
    public IActionResult GetProperties(string name) => new JsonResult(new { });
}
