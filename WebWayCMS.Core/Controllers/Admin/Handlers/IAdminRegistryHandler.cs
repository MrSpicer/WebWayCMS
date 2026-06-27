using Microsoft.AspNetCore.Mvc;

namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// Exposes a component/controller registry as admin JSON endpoints.
/// Implemented by handlers that have an associated registry (pages, contentzones).
/// </summary>
public interface IAdminRegistryHandler
{
    /// <summary>GET /admin/{contentType}/registry — returns all registered entries.</summary>
    IActionResult GetAll();

    /// <summary>GET /admin/{contentType}/registry/{name}/properties — returns properties for one entry.</summary>
    IActionResult GetProperties(string name);
}