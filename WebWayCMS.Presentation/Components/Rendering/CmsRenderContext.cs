using WebWayCMS.Data.Models;

namespace WebWayCMS.Presentation.Rendering;

/// <summary>
/// Ambient per-request rendering context cascaded to CMS Razor components.
/// Replaces the <c>HttpContext.Items["CMS:*"]</c> side-channel that the MVC
/// ViewComponents read for page/sub-route context.
/// </summary>
/// <param name="Page">The current page, or null when rendered without page context.</param>
/// <param name="Config">The deserialized page configuration object.</param>
/// <param name="SubRoute">The unmatched sub-route beneath the page, if any.</param>
public sealed record CmsRenderContext(PageDTO? Page, object? Config, string? SubRoute);
