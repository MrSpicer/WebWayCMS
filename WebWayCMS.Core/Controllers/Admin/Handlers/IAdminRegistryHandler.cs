using Microsoft.AspNetCore.Mvc;

namespace WebWayCMS.Controllers.Admin.Handlers;

/// <summary>
/// Exposes a component/controller registry as admin JSON endpoints.
/// Implemented by handlers that have an associated registry (pages, contentzones).
/// </summary>
public interface IAdminRegistryHandler
{
    /// <summary>GET /wadmin/{contentType}/registry — returns all registered entries.</summary>
    IActionResult GetAll();

    /// <summary>GET /wadmin/{contentType}/registry/{name}/properties — returns properties for one entry.</summary>
    IActionResult GetProperties(string name);

    /// <summary>
    /// POST /wadmin/{contentType}/registry/{name}/form — returns server-rendered form HTML
    /// for a given configuration instance, with optional pre-populated values.
    /// Default returns 404 so existing handlers compile without changes.
    /// </summary>
    IActionResult GetForm(string name, string? valuesJson) => new NotFoundResult();
}