using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Attributes;

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

    /// <summary>
    /// The form endpoint rendered for one widget display mode: only the configuration fields
    /// tagged for <paramref name="displayMode"/> are rendered (null renders every field). Default
    /// ignores the mode and delegates to <see cref="GetForm(string, string?)"/>, for registries
    /// whose entries have no display modes (page controllers).
    /// </summary>
    IActionResult GetForm(string name, string? valuesJson, WidgetDisplayMode? displayMode) => GetForm(name, valuesJson);
}