using Microsoft.AspNetCore.Mvc;

using WebWayCMS.Data.Models;

namespace WebWayCMS.Rendering;

/// <summary>
/// Renders a CMS page as a Blazor component response.
/// </summary>
/// <remarks>
/// The implementation lives in the Presentation layer (which owns the Razor components),
/// but the abstraction is declared here so page controllers in Core can return a
/// Blazor-rendered result without taking a reference on the Presentation assembly
/// (Presentation references Core, not the other way around).
/// </remarks>
public interface ICmsPageRenderer
{
    /// <summary>
    /// Produces an action result that renders the given page and its deserialized
    /// configuration through the CMS Blazor root component.
    /// </summary>
    /// <param name="page">The current page (may be null when no page context is present).</param>
    /// <param name="config">The deserialized page configuration object.</param>
    IActionResult RenderPage(PageDTO? page, object config);

    /// <summary>
    /// Renders an admin-only dynamic page (with admin chrome) through the CMS Blazor root component.
    /// When <paramref name="viewName"/> is "Dashboard" the admin dashboard is rendered; otherwise the
    /// page's "Main" content zone is rendered.
    /// </summary>
    IActionResult RenderAdminPage(PageDTO? page, object config, string? viewName);
}
